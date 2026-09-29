"""Regression checks for portrait squashing and seated support contacts."""
import importlib.util
import json
from pathlib import Path
import unittest
import numpy as np
import cv2
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('animation_import',ROOT/'tools/import-finished-animations.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class AnimationImportTests(unittest.TestCase):
    def test_portrait_is_contained_without_stretching(self):
        im=Image.new('RGBA',(80,120),(40,80,120,255))
        result=module.fit_square(im,384)
        self.assertEqual(result.getbbox(),(64,0,320,384))
        self.assertEqual(module.square_point(.5,1,im.size),[.5,1])
        self.assertEqual(module.square_point(0,0,im.size),[1/6,0])

    def test_all_portrait_exports_preserve_source_proportions(self):
        for key,(name,directional,_) in module.DEFS.items():
            if directional:continue
            frames,_,_=module.read_source(name)
            for i in (0,30,59):
                generated=Image.open(module.OUT/key/'left'/f'{i:04}.png').convert('RGBA')
                self.assertTrue(np.array_equal(np.array(generated),np.array(module.fit_square(frames[i],384))),f'{key}/{i}')

    def test_seat_contacts_follow_skirt_bottom_not_canvas_center(self):
        manifest=json.loads((module.OUT/'manifest.json').read_text('utf-8-sig'))
        for key in ('sit','sit_idle'):
            c=manifest['clips'][key];frames,_,_=module.read_source(c['label'])
            for anchor in c['anchors']:
                if key=='sit' and not 24<=anchor['frame']<42:continue
                measured=module.seat_contact(frames[anchor['frame']])
                self.assertLess(abs(measured[0]-anchor['x'])*384,5)
                self.assertLess(abs(measured[1]-anchor['y'])*384,3)
                self.assertGreater(anchor['y'],.78)

    def test_state_changes_keep_same_visible_standing_height(self):
        manifest=json.loads((module.OUT/'manifest.json').read_text('utf-8-sig'))
        for key in ('idle','walk','carry_idle','carry_walk','run'):
            c=manifest['clips'][key];frames,_,_=module.read_source(c['label'])
            measured=module.character_height(frames)*c['displayScale']*128
            self.assertAlmostEqual(measured,102.4,places=3,msg=key)
        self.assertEqual(manifest['clips']['sit']['displayScale'],manifest['clips']['sit_idle']['displayScale'])

    def test_wave_backdrop_removed_without_redrawing_character(self):
        originals,_,_=module.read_source('偷偷招手',processed=False)
        cleaned,_,_=module.read_source('偷偷招手')
        for i,(source,result) in enumerate(zip(originals,cleaned)):
            a,b=np.array(source),np.array(result)
            self.assertTrue(np.array_equal(a[:,:,:3],b[:,:,:3]))
            if not 22<=i<=51:self.assertTrue(np.array_equal(a,b))
            else:
                count,_,_,_=cv2.connectedComponentsWithStats((b[:,:,3]>0).astype('uint8'),8)
                self.assertEqual(count,2,f'isolated backing fragments in frame {i}')
        self.assertLess(np.count_nonzero(np.array(cleaned[35])[:,:,3]),np.count_nonzero(np.array(originals[35])[:,:,3])*.8)

    def test_sleep_contact_is_under_hands_and_loop_excludes_entry(self):
        manifest=json.loads((module.OUT/'manifest.json').read_text('utf-8-sig'))
        clip=manifest['clips']['lie_sleep']
        self.assertEqual(clip['loopStart'],30)
        self.assertTrue(clip['directional'])
        for a in clip['anchors']:
            if a['frame']<18:continue
            self.assertTrue(.22<a['x']<.4)
            self.assertTrue(.49<a['y']<.63)
        self.assertEqual(manifest['clips']['lifted']['pivotY'],.4)

if __name__=='__main__':unittest.main()
