import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { parseDocument } from 'yaml';

const MAX_SETTINGS_BYTES = 2 * 1024 * 1024;
// alpha.2 原型为 184 MiB；保留有限上限，不裁剪或重导出源资产。
const MAX_MODEL_BYTES = 256 * 1024 * 1024;
const MAX_RULES_BYTES = 256 * 1024;
const TEXT_FIELDS = new Set(['ai_name', 'ai_subtitle', 'title', 'info', 'system_prompt',
  'system_prompt_example', 'system_prompt_example_old', 'thinking_message']);
const VOICE_FIELDS = new Set(['tts_type', 'voice_lang', 'voice_dialect']);
const TRANSIENT = new Set(['character_id', 'resource_path', 'character_folder', 'script_key',
  'script_role_key', 'user_name', 'user_subtitle', 'voice_models', 'live2d', 'clothes', 'body_part']);
const DESKTOP = new Set(['scale', 'offset_x', 'offset_y', 'scale_p', 'offset_x_p', 'offset_y_p',
  'bubble_top', 'bubble_left', 'avatar_mode', 'avatar_mode_p', 'pet_frameless', 'clothes_name']);
const SENSITIVE_KEY = /(?:password|passwd|secret|token|credential|authorization|cookie|api[_-]?key|private[_-]?key|endpoint|url|path|filename|directory|folder)/i;
const UNSAFE_TEXT = /(?:[a-z]:[\\/]|\\\\[^\s\\]+\\|(?:^|[\s"'])(?:\/(?:Users|home|tmp|var|etc|mnt|Volumes)\/)|(?:https?|file|plugin):\/\/|\bBearer\s+\S+|\b(?:sk-[A-Za-z0-9_-]{10,}|ghp_[A-Za-z0-9_]{10,}|github_pat_[A-Za-z0-9_]{10,}|AIza[A-Za-z0-9_-]{20,})|-----BEGIN .*PRIVATE KEY-----|(?:api[_ -]?key|password|secret|token)\s*[:=])/im;
const UNSAFE_KEYS = new Set(['__proto__', 'constructor', 'prototype']);
const VRM_STRING_LOCATION = /^vrm\.(?:profile|motion\.(?:style|idle|propHands)|expressions\.(?:fallback|bindings\[\d+\]\.(?:emotion|expression|action|intent)|conversation\.gestures\[\d+\]|library\.presets\..+\.tags\[\d+\]))$/;
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const inside = (parent, child) => {
  const relative = path.relative(parent, child);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
};
const fail = (code, message) => { const e = new Error(message); e.code = code; throw e; };

async function statOrNull(filename) {
  try { return await fs.lstat(filename); } catch (e) { if (e.code === 'ENOENT') return null; throw e; }
}

// Validate every existing ancestor, including Windows junctions/reparse points.
// The PowerShell code is fixed; only base64 JSON data is inserted, never shell syntax from paths.
async function rejectLinks(filenames) {
  const existing = new Set();
  for (const filename of filenames) {
    let current = path.resolve(filename);
    while (true) {
      const stat = await statOrNull(current);
      if (stat) {
        if (stat.isSymbolicLink()) fail('REPARSE_POINT', 'Symbolic links and junctions are not accepted.');
        existing.add(current);
      }
      const parent = path.dirname(current);
      if (parent === current) break;
      current = parent;
    }
  }
  if (process.platform === 'win32' && existing.size) {
    const data = Buffer.from(JSON.stringify([...existing]), 'utf8').toString('base64');
    const script = `$ErrorActionPreference='Stop'; $paths=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('${data}'))|ConvertFrom-Json; foreach($p in $paths){$item=Get-Item -Force -LiteralPath $p; if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){exit 7}}`;
    try {
      execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')], { windowsHide: true, stdio: 'pipe' });
    } catch (e) {
      if (e.status === 7) fail('REPARSE_POINT', 'Windows reparse points are not accepted.');
      fail('PATH_CHECK_FAILED', 'Could not verify Windows path attributes; no import was written.');
    }
  }
}

function portableString(value) { return typeof value === 'string' && !UNSAFE_TEXT.test(value); }

// Unknown values remain verbatim in the private record. Portable unknown extensions
// accept data-only values; unknown strings require a later explicitly supported schema.
function safeUnknown(value, key, omitted, location) {
  if (UNSAFE_KEYS.has(key) || SENSITIVE_KEY.test(key) || !portableString(key) || key.length > 128) { omitted.push(location); return undefined; }
  if (value === null || typeof value === 'boolean' || (typeof value === 'number' && Number.isFinite(value))) return value;
  if (Array.isArray(value)) {
    const result = value.map((item, i) => safeUnknown(item, String(i), omitted, `${location}[${i}]`));
    if (result.some(item => item === undefined)) { omitted.push(location); return undefined; }
    return result;
  }
  if (value && typeof value === 'object') {
    const result = {};
    for (const [childKey, item] of Object.entries(value)) {
      const clean = safeUnknown(item, childKey, omitted, `${location}.${childKey}`);
      if (clean !== undefined) result[childKey] = clean;
    }
    return result;
  }
  omitted.push(location);
  return undefined;
}

// Known VRM semantics are retained for future adapters, without executing them.
// String IDs are bounded and screened; endpoint/path-bearing keys are not portable.
function safeVrm(value, omitted, location = 'vrm', depth = 0) {
  if (depth > 32) fail('SETTINGS_TOO_DEEP', 'VRM settings exceed the nesting limit.');
  if (value === null || typeof value === 'boolean' || (typeof value === 'number' && Number.isFinite(value))) return value;
  if (typeof value === 'string') {
    if (VRM_STRING_LOCATION.test(location) && portableString(value) && value.length <= 512) return value;
    omitted.push(location); return undefined;
  }
  if (Array.isArray(value)) {
    const result = value.map((item, i) => safeVrm(item, omitted, `${location}[${i}]`, depth + 1));
    if (result.some(item => item === undefined)) { omitted.push(location); return undefined; }
    return result;
  }
  if (value && typeof value === 'object') {
    const result = {};
    for (const [key, item] of Object.entries(value)) {
      if (UNSAFE_KEYS.has(key) || SENSITIVE_KEY.test(key) || !portableString(key) || key.length > 128 || (location === 'vrm' && key === 'model')) { omitted.push(`${location}.${key}`); continue; }
      const clean = safeVrm(item, omitted, `${location}.${key}`, depth + 1);
      if (clean !== undefined) result[key] = clean;
    }
    return result;
  }
  omitted.push(location); return undefined;
}

export function parseSettings(bytes) {
  if (bytes.length > MAX_SETTINGS_BYTES) fail('SETTINGS_TOO_LARGE', 'settings.yml exceeds 2 MiB.');
  const document = parseDocument(bytes.toString('utf8'), {
    version: '1.2', uniqueKeys: true, stringKeys: true, prettyErrors: false,
  });
  if (document.errors.length || document.warnings.length) fail('INVALID_YAML', 'settings.yml contains YAML errors, unsupported tags, or ambiguous syntax.');
  let data;
  try { data = document.toJS({ maxAliasCount: 20 }); }
  catch { fail('INVALID_YAML', 'settings.yml aliases could not be safely resolved.'); }
  if (!data || typeof data !== 'object' || Array.isArray(data)) fail('INVALID_SETTINGS', 'settings.yml must contain one mapping.');
  // Preserve JSON type fidelity and reject cycles/non-finite values before writing records.
  const visit = (value, ancestors = new Set(), depth = 0) => {
    if (depth > 48) fail('SETTINGS_TOO_DEEP', 'settings.yml exceeds the nesting limit.');
    if (typeof value === 'number' && !Number.isFinite(value)) fail('INVALID_SETTINGS', 'Non-finite numbers are not supported.');
    if (value && typeof value === 'object') {
      if (ancestors.has(value)) fail('INVALID_SETTINGS', 'Recursive YAML aliases are not supported.');
      const next = new Set(ancestors).add(value);
      for (const child of Object.values(value)) visit(child, next, depth + 1);
    }
  };
  visit(data);
  return data;
}

export function inspectVrm(bytes) {
  if (bytes.length > MAX_MODEL_BYTES || bytes.length < 20 || bytes.readUInt32LE(0) !== 0x46546c67 || bytes.readUInt32LE(4) !== 2 || bytes.readUInt32LE(8) !== bytes.length) fail('INVALID_VRM', 'Model must be a complete GLB 2 VRM of at most 256 MiB.');
  const length = bytes.readUInt32LE(12);
  if (bytes.readUInt32LE(16) !== 0x4e4f534a || length % 4 || 20 + length > bytes.length) fail('INVALID_VRM', 'VRM JSON chunk is invalid.');
  let gltf;
  try { gltf = JSON.parse(bytes.subarray(20, 20 + length).toString('utf8')); }
  catch { fail('INVALID_VRM', 'VRM JSON chunk cannot be parsed.'); }
  const vrm1 = gltf.extensions?.VRMC_vrm;
  const vrm0 = gltf.extensions?.VRM;
  if (!vrm1 && !vrm0) fail('INVALID_VRM', 'GLB has no VRM extension.');
  if (vrm1 && vrm1.specVersion !== '1.0') fail('UNSUPPORTED_VRM', 'Only VRM 1.0 or legacy VRM 0.x is supported.');
  if ([...(gltf.buffers ?? []), ...(gltf.images ?? [])].some(item => item.uri && !item.uri.startsWith('data:'))) fail('EXTERNAL_MODEL_RESOURCE', 'Model references external resources; a self-contained VRM is required.');
  const expressions = vrm1 ? Object.keys(vrm1.expressions?.preset ?? {}) : (vrm0.blendShapeMaster?.blendShapeGroups ?? []).map(group => group.presetName).filter(Boolean);
  return {
    format: vrm1 ? 'vrm-1.0' : 'vrm-0.x',
    basicExpressions: expressions,
    animationsEmbedded: (gltf.animations ?? []).length,
    nodes: (gltf.nodes ?? []).length,
    materials: (gltf.materials ?? []).length,
    textures: (gltf.textures ?? []).length,
  };
}

async function readRegular(filename, maxBytes) {
  const stat = await fs.lstat(filename);
  if (!stat.isFile() || stat.isSymbolicLink()) fail('INVALID_FILE', 'Expected a regular file.');
  if (stat.size > maxBytes) fail('FILE_TOO_LARGE', 'Input exceeds the allowed size.');
  return fs.readFile(filename);
}

function resolveModel(source, relative) {
  if (typeof relative !== 'string' || !relative || path.win32.isAbsolute(relative) || path.posix.isAbsolute(relative) || /^[A-Za-z]:/.test(relative) || /[:\0]/.test(relative)) fail('MODEL_PATH_ESCAPE', 'vrm.model must be a relative file path.');
  const pieces = relative.split(/[\\/]/);
  if (pieces.some(piece => piece === '..' || piece === '' || piece === '.')) fail('MODEL_PATH_ESCAPE', 'vrm.model contains traversal or empty path components.');
  const resolved = path.resolve(source, ...pieces);
  if (!inside(source, resolved)) fail('MODEL_PATH_ESCAPE', 'vrm.model escapes the character directory.');
  return resolved;
}

async function atomicWrite(filename, bytes) {
  const temporary = `${filename}.${randomUUID()}.tmp`;
  await fs.writeFile(temporary, bytes, { flag: 'wx' });
  try { await fs.rename(temporary, filename); }
  catch (e) { await fs.unlink(temporary).catch(() => {}); throw e; }
}

export async function importCharacter(options) {
  if (!options.source || !options.output || !options.records) fail('MISSING_ARGUMENT', '--source, --output and --records are required.');
  const source = path.resolve(options.source);
  const output = path.resolve(options.output);
  const records = path.resolve(options.records);
  for (const [a, b] of [[source, output], [source, records], [output, records]]) {
    if (inside(a, b) || inside(b, a)) fail('OVERLAPPING_DIRECTORIES', 'Source, portable output and private records must be separate, non-nested directories.');
  }
  const sourceId = sha(Buffer.from(process.platform === 'win32' ? source.toLowerCase() : source));
  const characterId = options.characterId ?? sourceId.slice(0, 16);
  if (!/^[a-z0-9][a-z0-9_-]{0,63}$/.test(characterId)) fail('INVALID_CHARACTER_ID', 'Character ID must contain 1–64 lowercase letters, digits, hyphens or underscores.');
  const settingsPath = path.join(source, 'settings.yml');
  const packageRoot = path.join(output, characterId);
  const recordRoot = path.join(records, characterId);
  await rejectLinks([settingsPath, packageRoot, recordRoot]);
  const settingsBytes = await readRegular(settingsPath, MAX_SETTINGS_BYTES);
  const settingsSha256 = sha(settingsBytes);
  const settings = parseSettings(settingsBytes);
  const omitted = [];
  const character = {};
  const voiceReference = {};
  const extensions = {};
  for (const [key, value] of Object.entries(settings)) {
    if (TEXT_FIELDS.has(key) || VOICE_FIELDS.has(key)) {
      if (value === null || portableString(value)) (TEXT_FIELDS.has(key) ? character : voiceReference)[key] = value;
      else omitted.push(key);
    } else if (key === 'vrm' || TRANSIENT.has(key) || DESKTOP.has(key)) {
      if (key !== 'vrm') omitted.push(key);
    } else {
      const clean = safeUnknown(value, key, omitted, key);
      if (clean !== undefined) extensions[key] = clean;
    }
  }
  const missing = [];
  let modelSource = null;
  let isPlaceholder = false;
  const binding = settings.vrm?.model;
  if (binding !== undefined && binding !== null && binding !== '') {
    modelSource = resolveModel(source, binding);
    await rejectLinks([modelSource]);
    if (!(await statOrNull(modelSource))) {
      missing.push({ code: 'BOUND_MODEL_MISSING', message: 'The character references a model file that is absent.' });
      modelSource = null;
    }
  } else missing.push({ code: 'CHARACTER_HAS_NO_3D_MODEL', message: 'The source character has no VRM model binding.' });
  if (!modelSource && options.placeholder) {
    modelSource = path.resolve(options.placeholder);
    if (inside(output, modelSource) || inside(records, modelSource)) fail('PLACEHOLDER_IN_OUTPUT', 'Placeholder must be an independent source asset.');
    await rejectLinks([modelSource]);
    isPlaceholder = true;
  }
  let modelBytes = null;
  let model = null;
  let inspection = null;
  if (modelSource) {
    modelBytes = await readRegular(modelSource, MAX_MODEL_BYTES);
    inspection = inspectVrm(modelBytes);
    const modelSha256 = sha(modelBytes);
    if (options.expectedModelSha256 && options.expectedModelSha256.toLowerCase() !== modelSha256) fail('MODEL_HASH_MISMATCH', 'Model does not match --expected-model-sha256.');
    model = { path: 'model.vrm', sha256: modelSha256, bytes: modelBytes.length, format: inspection.format, isPlaceholder,
      sourceBinding: isPlaceholder ? 'missing' : 'explicit' };
  }
  let sourceRulesPath = null;
  let sourceRulesBytes = null;
  if (options.sourceRules) {
    sourceRulesPath = path.resolve(options.sourceRules);
    if (inside(output, sourceRulesPath) || inside(records, sourceRulesPath)) fail('RULES_IN_OUTPUT', 'Source rules must come from an independent source file.');
    await rejectLinks([sourceRulesPath]);
    sourceRulesBytes = await readRegular(sourceRulesPath, MAX_RULES_BYTES);
  }
  const previousRecordPath = path.join(recordRoot, 'latest.json');
  const calibrationPath = path.join(recordRoot, 'calibration.json');
  await rejectLinks([previousRecordPath, calibrationPath, path.join(packageRoot, 'manifest.json'), path.join(packageRoot, 'model.vrm'), path.join(packageRoot, 'source-rules.txt')]);
  if (await statOrNull(previousRecordPath)) {
    const previous = JSON.parse(await fs.readFile(previousRecordPath, 'utf8'));
    if (previous.sourceId !== sourceId) fail('CHARACTER_ID_CONFLICT', 'This character ID already belongs to a different source directory.');
  }
  if (await statOrNull(packageRoot)) {
    const children = await fs.readdir(packageRoot);
    const owned = ['manifest.json', 'model.vrm', 'source-rules.txt'];
    const allowed = new Set([...owned, ...owned.map(name => `${name}.meta`)]);
    if (children.some(name => !allowed.has(name))) fail('OUTPUT_NOT_OWNED', 'Existing package contains files not managed by this importer.');
    await rejectLinks(children.map(name => path.join(packageRoot, name)));
    if (children.length && !(await statOrNull(path.join(packageRoot, 'manifest.json')))) fail('OUTPUT_NOT_OWNED', 'Existing output is not an importer package.');
    if (children.length) {
      const old = JSON.parse(await fs.readFile(path.join(packageRoot, 'manifest.json'), 'utf8'));
      if (old.generator !== 'lingchat-xr-character-import/0.1.0' || old.source?.sourceId !== sourceId) fail('OUTPUT_NOT_OWNED', 'Existing output is not this character importer package.');
      if (children.includes('source-rules.txt')) {
        if (!old.assets?.some(asset => asset.path === 'source-rules.txt' && asset.kind === 'source-rules')) fail('OUTPUT_NOT_OWNED', 'Existing source-rules.txt is not registered to the importer.');
        if (!sourceRulesBytes) fail('SOURCE_RULES_REQUIRED', 'Repeat the explicit --source-rules argument; existing rules must not be silently discarded or assigned to a different model.');
      }
    }
  }
  let xrCalibration = { status: 'required', modelSha256: model?.sha256 ?? null };
  if (await statOrNull(calibrationPath)) {
    const calibration = JSON.parse(await fs.readFile(calibrationPath, 'utf8'));
    if (model && calibration.modelSha256 === model.sha256) {
      const numeric = ['heightMeters', 'areaRadiusMeters'];
      if (calibration.schemaVersion === 1 && numeric.every(key => typeof calibration[key] === 'number' && Number.isFinite(calibration[key]) && calibration[key] > 0)) {
        xrCalibration = { status: 'valid', modelSha256: model.sha256, heightMeters: calibration.heightMeters, areaRadiusMeters: calibration.areaRadiusMeters };
      }
    } else xrCalibration.status = 'invalidated';
  }
  const manifest = {
    schemaVersion: 1,
    generator: 'lingchat-xr-character-import/0.1.0',
    sourceFormat: 'lingchat-settings-yaml',
    source: { sourceId, settingsSha256, vrmSettingsVersion: Number.isInteger(settings.vrm?.version) ? settings.vrm.version : null },
    characterId,
    displayName: typeof character.ai_name === 'string' && character.ai_name ? character.ai_name : characterId,
    character,
    voiceReference,
    model,
    assets: [
      ...(model ? [{ path: model.path, sha256: model.sha256, bytes: model.bytes, kind: 'model' }] : []),
      ...(sourceRulesBytes ? [{ path: 'source-rules.txt', sha256: sha(sourceRulesBytes), bytes: sourceRulesBytes.length, kind: 'source-rules' }] : []),
    ],
    lingchatVrm: settings.vrm == null ? null : safeVrm(settings.vrm, omitted),
    extensions,
    capabilities: { hasModel: !!model, basicExpressions: inspection?.basicExpressions ?? [], animationsEmbedded: inspection?.animationsEmbedded ?? 0,
      lingchatExpressionsExecution: 'basic-vrm-only', nativeMotionsExecution: 'not-implemented', ttsExecution: 'separately-configured' },
    missing,
    xrCalibration,
    omittedFields: [...new Set(omitted.map(field => portableString(field) ? field : '<unsafe-field-name>'))].sort(),
  };
  // Validate all inputs before any writes. Only exact outputs and private records are written.
  await fs.mkdir(recordRoot, { recursive: true });
  const lockPath = path.join(recordRoot, 'import.lock');
  let lock;
  try { lock = await fs.open(lockPath, 'wx'); }
  catch (e) { if (e.code === 'EEXIST') fail('IMPORT_BUSY', 'An import lock exists; inspect the prior run before removing it.'); throw e; }
  try {
    const snapshotRoot = path.join(recordRoot, 'source', settingsSha256);
    await rejectLinks([snapshotRoot]);
    await fs.mkdir(snapshotRoot, { recursive: true });
    const snapshotPath = path.join(snapshotRoot, 'settings.yml');
    const parsedPath = path.join(snapshotRoot, 'parsed.json');
    await rejectLinks([snapshotPath, parsedPath]);
    if (!(await statOrNull(snapshotPath))) await fs.writeFile(snapshotPath, settingsBytes, { flag: 'wx' });
    else if (sha(await fs.readFile(snapshotPath)) !== settingsSha256) fail('RECORD_CORRUPT', 'Private source snapshot no longer matches its hash.');
    if (!(await statOrNull(parsedPath))) await fs.writeFile(parsedPath, JSON.stringify(settings, null, 2), { flag: 'wx' });
    await fs.mkdir(packageRoot, { recursive: true });
    if (modelBytes) {
      await atomicWrite(path.join(packageRoot, 'model.vrm'), modelBytes);
      if (sha(await fs.readFile(path.join(packageRoot, 'model.vrm'))) !== model.sha256) fail('COPY_HASH_MISMATCH', 'Copied model failed SHA256 verification.');
    } else if (await statOrNull(path.join(packageRoot, 'model.vrm'))) await fs.unlink(path.join(packageRoot, 'model.vrm'));
    if (sourceRulesBytes) {
      await atomicWrite(path.join(packageRoot, 'source-rules.txt'), sourceRulesBytes);
      if (sha(await fs.readFile(path.join(packageRoot, 'source-rules.txt'))) !== sha(sourceRulesBytes)) fail('COPY_HASH_MISMATCH', 'Copied source rules failed SHA256 verification.');
    }
    // The manifest is committed after the model. A failed import cannot silently load mismatched bytes.
    await atomicWrite(path.join(packageRoot, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
    if (sha(await fs.readFile(settingsPath)) !== settingsSha256 || (modelSource && sha(await fs.readFile(modelSource)) !== model.sha256) || (sourceRulesPath && sha(await fs.readFile(sourceRulesPath)) !== sha(sourceRulesBytes))) fail('SOURCE_CHANGED_DURING_IMPORT', 'Source changed concurrently; repeat the import against a stable source.');
    await atomicWrite(previousRecordPath, `${JSON.stringify({ sourceId, sourceDirectory: source, settingsSha256, modelSource, modelSha256: model?.sha256 ?? null, importedAt: new Date().toISOString() }, null, 2)}\n`);
    return { manifest, packageRoot, recordRoot };
  } finally {
    await lock.close();
    await fs.unlink(lockPath);
  }
}

function cliOptions(argv) {
  const mapping = { '--source': 'source', '--output': 'output', '--records': 'records', '--character-id': 'characterId', '--placeholder': 'placeholder', '--expected-model-sha256': 'expectedModelSha256', '--source-rules': 'sourceRules' };
  const result = {};
  for (let i = 0; i < argv.length; i += 2) {
    if (!mapping[argv[i]] || !argv[i + 1] || argv[i + 1].startsWith('--')) fail('INVALID_ARGUMENT', `Unknown option or missing value: ${argv[i]}`);
    result[mapping[argv[i]]] = argv[i + 1];
  }
  return result;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const result = await importCharacter(cliOptions(process.argv.slice(2)));
    console.log(JSON.stringify({ characterId: result.manifest.characterId, packageRoot: result.packageRoot, hasModel: result.manifest.capabilities.hasModel, modelSha256: result.manifest.model?.sha256 ?? null, isPlaceholder: result.manifest.model?.isPlaceholder ?? false, calibration: result.manifest.xrCalibration.status, missing: result.manifest.missing }, null, 2));
  } catch (error) {
    console.error(JSON.stringify({ error: error.code ?? 'IMPORT_FAILED', message: error.code ? error.message : 'Import failed; inspect paths and files locally. Private source values were not printed.' }));
    process.exitCode = 1;
  }
}
