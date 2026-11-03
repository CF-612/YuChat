"""Offline verification for the my-neuro adapter; no inference weights required."""
import json
from pathlib import Path
import struct
import tempfile
import unittest

import numpy as np
from convert_t2m import AXES, PARENT, convert


def fixture():
    pose = AXES.astype(float) * .15
    for j,parent in enumerate(PARENT):
        if parent >= 0:
            pose[j] += pose[parent]
    frames = []
    for phase in np.linspace(0, 2*np.pi, 60):
        joints = pose.copy()
        joints[:,1] += .02*np.sin(phase)
        joints[20,1] += .1*np.sin(phase)
        frames.append(joints.tolist())
    return {"fps":20, "joints":frames}


class T2mTest(unittest.TestCase):
    def test_converts_body_with_external_root(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"body.vrma"
            convert(fixture(),path)
            raw=path.read_bytes()
            size=struct.unpack_from("<I",raw,12)[0]
            doc=json.loads(raw[20:20+size])
            humanoid=doc["extensions"]["VRMC_vrm_animation"]["humanoid"]["humanBones"]
            self.assertEqual(len(humanoid),22)
            track=doc["animations"][0]
            translation=[c for c in track["channels"] if c["target"]["path"]=="translation"]
            self.assertEqual(len(translation),1)
            self.assertEqual(translation[0]["target"]["node"],humanoid["hips"]["node"])
            sampler=track["samplers"][translation[0]["sampler"]]
            accessor=doc["accessors"][sampler["output"]]
            offset=doc["bufferViews"][accessor["bufferView"]]["byteOffset"]
            values=np.frombuffer(raw,dtype="<f4",offset=28+size+offset,count=180).reshape(-1,3)
            self.assertTrue(np.all(values[:,[0,2]]==0))
            self.assertGreater(np.ptp(values[:,1]),.03)

    def test_rejects_bad_data_before_writing(self):
        for value in [0,float("nan")]:
            data=fixture()
            data["joints"][0][4]=[x+value for x in data["joints"][0][1]]
            with tempfile.TemporaryDirectory() as folder:
                path=Path(folder)/"body.vrma"
                with self.assertRaises(ValueError):
                    convert(data,path)
                self.assertFalse(path.exists())


if __name__=="__main__":
    unittest.main()
