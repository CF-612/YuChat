import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { importCharacter, parseSettings, inspectVrm } from './import.mjs';
import { validatePresentationProfile } from './presentation-profile.mjs';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function modelBytes(marker = 'one') {
  const json = Buffer.from(JSON.stringify({ asset: { version: '2.0' }, extensions: {
    VRMC_vrm: { specVersion: '1.0', meta: { name: marker }, expressions: { preset: { happy: {}, blink: {} } } },
  }, nodes: [], materials: [], textures: [] }));
  const padded = Buffer.concat([json, Buffer.alloc((4 - json.length % 4) % 4, 32)]);
  const header = Buffer.alloc(20);
  header.writeUInt32LE(0x46546c67, 0); header.writeUInt32LE(2, 4);
  header.writeUInt32LE(20 + padded.length, 8); header.writeUInt32LE(padded.length, 12);
  header.writeUInt32LE(0x4e4f534a, 16);
  return Buffer.concat([header, padded]);
}

async function fixture(t, yaml = 'ai_name: Fixture\n') {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lingchat-xr-import-test-'));
  const resolvedRoot = path.resolve(root);
  const resolvedTemp = path.resolve(os.tmpdir());
  t.after(async () => {
    const relative = path.relative(resolvedTemp, resolvedRoot);
    assert.ok(relative.startsWith('lingchat-xr-import-test-') && !relative.includes(path.sep));
    await fs.rm(resolvedRoot, { recursive: true, force: true });
  });
  const source = path.join(root, 'source');
  await fs.mkdir(source);
  await fs.writeFile(path.join(source, 'settings.yml'), yaml);
  const placeholder = path.join(root, 'placeholder.vrm');
  await fs.writeFile(placeholder, modelBytes());
  return { root, source, placeholder, output: path.join(root, 'portable'), records: path.join(root, 'private'), characterId: 'fixture' };
}

test('YAML preserves false, null, empty arrays, none, quoted booleans and unknown values', () => {
  const parsed = parseSettings(Buffer.from('disabled: false\nempty: []\nmissing: null\naction: none\nquoted: "false"\nfuture: {weight: 0}\n'));
  assert.deepEqual(parsed, { disabled: false, empty: [], missing: null, action: 'none', quoted: 'false', future: { weight: 0 } });
  assert.throws(() => parseSettings(Buffer.from('a: 1\na: 2')), { code: 'INVALID_YAML' });
  assert.throws(() => parseSettings(Buffer.from('a: &cycle {b: *cycle}')), { code: 'INVALID_SETTINGS' });
});

test('private source bytes remain outside the portable package; safe semantics are retained', async t => {
  const yaml = `# keep this comment exactly\nai_name: Fixture\nsystem_prompt: An invented fixture persona.\nuser_name: private-name\napi_key: private-secret\nfuture: {enabled: false, values: [], label: private-note}\nvoice_models: {gsv_voice_filename: 'D:\\private\\voice.wav'}\nvrm:\n  version: 1\n  scale: 9\n  offset_y: 4\n  future_text: private-vrm-note\n  motion: {genericEnabled: false, idle: none, future: {enabled: false}}\n  expressions:\n    fallback: null\n    conversation: {enabled: false, gestures: []}\n    library: {enabled: false, presets: {unknown-id: {tags: []}}}\n`;
  const options = await fixture(t, yaml);
  await fs.appendFile(path.join(options.source, 'settings.yml'), "info: 'C:\\private\\machine.txt'\nai_subtitle: sk-syntheticfixturecredential\n");
  const originalYaml = await fs.readFile(path.join(options.source, 'settings.yml'), 'utf8');
  const before = hash(await fs.readFile(path.join(options.source, 'settings.yml')));
  const result = await importCharacter(options);
  assert.equal(result.manifest.model.isPlaceholder, true);
  assert.equal(result.manifest.model.sourceBinding, 'missing');
  assert.equal(result.manifest.missing[0].code, 'CHARACTER_HAS_NO_3D_MODEL');
  assert.equal(result.manifest.lingchatVrm.motion.idle, 'none');
  assert.equal(result.manifest.lingchatVrm.motion.genericEnabled, false);
  assert.deepEqual(result.manifest.lingchatVrm.expressions.conversation.gestures, []);
  assert.equal(result.manifest.lingchatVrm.expressions.fallback, null);
  assert.deepEqual(result.manifest.extensions.future, { enabled: false, values: [] });
  assert.equal(result.manifest.xrCalibration.status, 'required');
  const portable = await fs.readFile(path.join(result.packageRoot, 'manifest.json'), 'utf8');
  for (const privateValue of ['private-name', 'private-secret', 'private-note', 'private-vrm-note', 'D:\\\\private', 'machine.txt', 'sk-syntheticfixturecredential', options.source]) assert.ok(!portable.includes(privateValue));
  const raw = await fs.readFile(path.join(result.recordRoot, 'source', before, 'settings.yml'), 'utf8');
  assert.equal(raw, originalYaml);
  const parsed = JSON.parse(await fs.readFile(path.join(result.recordRoot, 'source', before, 'parsed.json'), 'utf8'));
  assert.equal(parsed.future.label, 'private-note');
  assert.equal(hash(await fs.readFile(path.join(options.source, 'settings.yml'))), before);
  assert.equal(hash(await fs.readFile(path.join(result.packageRoot, 'model.vrm'))), hash(await fs.readFile(options.placeholder)));
  assert.deepEqual((await fs.readdir(result.packageRoot)).sort(), ['manifest.json', 'model.vrm']);
});

test('missing 3D without an explicit placeholder stays missing, with a portable manifest', async t => {
  const options = await fixture(t);
  delete options.placeholder;
  const result = await importCharacter(options);
  assert.equal(result.manifest.model, null);
  assert.equal(result.manifest.capabilities.hasModel, false);
  assert.equal(result.manifest.missing[0].code, 'CHARACTER_HAS_NO_3D_MODEL');
  assert.deepEqual(await fs.readdir(result.packageRoot), ['manifest.json']);
});

test('an explicit relative VRM binding is used; other resource-looking fields are not followed', async t => {
  const options = await fixture(t, "ai_name: Fixture\nvrm: {version: 1, model: 'vrm/own.vrm'}\ncustom_resource: '../do-not-read.txt'\n");
  await fs.mkdir(path.join(options.source, 'vrm'));
  await fs.writeFile(path.join(options.source, 'vrm', 'own.vrm'), modelBytes('bound'));
  const result = await importCharacter(options);
  assert.equal(result.manifest.model.isPlaceholder, false);
  assert.equal(result.manifest.model.sourceBinding, 'explicit');
  assert.equal(result.manifest.model.sha256, hash(modelBytes('bound')));
  assert.deepEqual(result.manifest.missing, []);
  assert.equal(result.manifest.extensions.custom_resource, undefined);
});

test('relative traversal, Windows absolute paths, drive-relative and ADS paths are rejected', async t => {
  const options = await fixture(t);
  for (const model of ['../outside.vrm', 'vrm/../../outside.vrm', 'D:/outside.vrm', 'C:relative.vrm', '//server/share/model.vrm', '/tmp/model.vrm', 'vrm/model.vrm:secret']) {
    await fs.writeFile(path.join(options.source, 'settings.yml'), `vrm: {model: '${model}'}\n`);
    await assert.rejects(importCharacter(options), { code: 'MODEL_PATH_ESCAPE' });
  }
  assert.equal(await fs.stat(options.output).catch(() => null), null);
});

test('source, portable output and private records cannot overlap', async t => {
  const options = await fixture(t);
  await assert.rejects(importCharacter({ ...options, records: path.join(options.output, 'records') }), { code: 'OVERLAPPING_DIRECTORIES' });
  await assert.rejects(importCharacter({ ...options, output: path.join(options.source, 'export') }), { code: 'OVERLAPPING_DIRECTORIES' });
  await assert.rejects(importCharacter({ ...options, records: options.source }), { code: 'OVERLAPPING_DIRECTORIES' });
});

test('symlink/junction resources cannot escape the source root', async t => {
  const options = await fixture(t, "vrm: {model: 'linked/model.vrm'}\n");
  const outside = path.join(options.root, 'outside');
  await fs.mkdir(outside);
  await fs.writeFile(path.join(outside, 'model.vrm'), modelBytes());
  await fs.symlink(outside, path.join(options.source, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(importCharacter(options), { code: 'REPARSE_POINT' });
});

test('model SHA mismatch and non-VRM bytes fail before output is written', async t => {
  const options = await fixture(t);
  await assert.rejects(importCharacter({ ...options, expectedModelSha256: '0'.repeat(64) }), { code: 'MODEL_HASH_MISMATCH' });
  assert.equal(await fs.stat(options.output).catch(() => null), null);
  await fs.writeFile(options.placeholder, 'not a VRM');
  await assert.rejects(importCharacter(options), { code: 'INVALID_VRM' });
  assert.equal(await fs.stat(options.output).catch(() => null), null);
  assert.equal(inspectVrm(modelBytes()).format, 'vrm-1.0');
});

test('repeat imports preserve local XR calibration; a new model SHA invalidates it', async t => {
  const options = await fixture(t);
  const first = await importCharacter(options);
  const calibrationPath = path.join(first.recordRoot, 'calibration.json');
  const calibrationBytes = JSON.stringify({ schemaVersion: 1, modelSha256: first.manifest.model.sha256, heightMeters: 0.18, areaRadiusMeters: 0.22, privateFutureField: 'keep verbatim' }, null, 4);
  await fs.writeFile(calibrationPath, calibrationBytes);
  const metaPath = path.join(first.packageRoot, 'model.vrm.meta');
  await fs.writeFile(metaPath, 'fileFormatVersion: 2\nguid: test-unchanged-guid\n');
  const second = await importCharacter(options);
  assert.equal(second.manifest.xrCalibration.status, 'valid');
  assert.equal(second.manifest.xrCalibration.heightMeters, 0.18);
  assert.equal(await fs.readFile(calibrationPath, 'utf8'), calibrationBytes);
  assert.equal(await fs.readFile(metaPath, 'utf8'), 'fileFormatVersion: 2\nguid: test-unchanged-guid\n');
  await fs.writeFile(options.placeholder, modelBytes('replacement'));
  const third = await importCharacter(options);
  assert.equal(third.manifest.xrCalibration.status, 'invalidated');
  assert.equal(third.manifest.xrCalibration.heightMeters, undefined);
  assert.notEqual(third.manifest.model.sha256, first.manifest.model.sha256);
  assert.equal(await fs.readFile(calibrationPath, 'utf8'), calibrationBytes);
});

test('existing output is not overwritten when it contains unowned files', async t => {
  const options = await fixture(t);
  const first = await importCharacter(options);
  await fs.writeFile(path.join(first.packageRoot, 'user-file.txt'), 'keep');
  await assert.rejects(importCharacter(options), { code: 'OUTPUT_NOT_OWNED' });
  assert.equal(await fs.readFile(path.join(first.packageRoot, 'user-file.txt'), 'utf8'), 'keep');
});

test('source identity cannot be replaced by reusing an explicit character ID', async t => {
  const options = await fixture(t);
  await importCharacter(options);
  const otherSource = path.join(options.root, 'other-source');
  await fs.mkdir(otherSource);
  await fs.writeFile(path.join(otherSource, 'settings.yml'), 'ai_name: Other\n');
  await assert.rejects(importCharacter({ ...options, source: otherSource }), { code: 'CHARACTER_ID_CONFLICT' });
});

test('explicit source rules and credits travel with the model and are hash-declared', async t => {
  const options = await fixture(t);
  const sourceRules = path.join(options.source, 'original-rules.txt');
  const bytes = Buffer.from('Fixture author: Example Artist. Local development only.\n');
  await fs.writeFile(sourceRules, bytes);
  const first = await importCharacter({ ...options, sourceRules });
  assert.deepEqual(await fs.readFile(path.join(first.packageRoot, 'source-rules.txt')), bytes);
  assert.deepEqual(first.manifest.assets.find(asset => asset.kind === 'source-rules'), {
    path: 'source-rules.txt', sha256: hash(bytes), bytes: bytes.length, kind: 'source-rules',
  });
  await assert.rejects(importCharacter(options), { code: 'SOURCE_RULES_REQUIRED' });
  const repeated = await importCharacter({ ...options, sourceRules });
  assert.equal(repeated.manifest.assets.length, 2);
});

test('presentation profile is explicit, portable and preserved on repeated import', async t => {
  const options=await fixture(t);
  const profile=path.join(options.root,'profile.json');
  const bytes=await fs.readFile(new URL('./profiles/smallbluefish-face-v1.json',import.meta.url));
  await fs.writeFile(profile,bytes);
  const result=await importCharacter({...options,presentationProfile:profile});
  assert.equal(result.manifest.presentationProfile,'presentation.json');
  assert.equal(result.manifest.assets.find(a=>a.kind==='presentation-profile').sha256,hash(bytes));
  assert.deepEqual(await fs.readFile(path.join(result.packageRoot,'presentation.json')),bytes);
  await assert.rejects(importCharacter(options),{code:'PRESENTATION_PROFILE_REQUIRED'});
  await importCharacter({...options,presentationProfile:profile});
  const bad=JSON.parse(bytes);bad.expressions[0].semantic='typo';await fs.writeFile(profile,JSON.stringify(bad));
  await assert.rejects(importCharacter({...options,presentationProfile:profile}),{code:'INVALID_PRESENTATION_PROFILE'});
  assert.deepEqual(await fs.readFile(path.join(result.packageRoot,'presentation.json')),bytes);
});

test('presentation profile rejects undeclared fields instead of publishing private extensions', async () => {
  const profile=JSON.parse(await fs.readFile(new URL('./profiles/smallbluefish-face-v1.json',import.meta.url),'utf8'));
  assert.doesNotThrow(()=>validatePresentationProfile(profile));
  for(const value of [{...profile,privateNote:'private-value'},{...profile,expressions:[...profile.expressions,{semantic:'new',key:'key',path:'private'}]}])assert.throws(()=>validatePresentationProfile(value));
});
