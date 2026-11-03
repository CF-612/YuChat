# 第一批面部能力实查

下表从本机实际 VRM 的 VRMC_vrm.expressions 提取。绑定数包含 morphTarget、materialColor、textureTransform；override 顺序为眨眼 / 发音。部位按实际绑定的节点和目标名列出代表项。

## local-smallbluefish-alpha2

SHA-256 `5d5762143b093f71bfd513b69449f36097a7300e414bcc1b84853604b6ea3e2d`

|键|绑定数|override|代表绑定|
|---|---:|---|---|
|aa|45|none / none|SharedNaturalBlush_Left / FollowFace_aa、SharedNaturalBlush_Right / FollowFace_aa、CheekSlashes_Left / SlashFollow_aa|
|blink|30|none / none|donor_tripo_part_1 / blink、SharedNaturalBlush_Left / FollowFace_blink、SharedNaturalBlush_Right / FollowFace_blink|
|happy|41|block / blend|donor_tripo_part_1 / browHappy、donor_tripo_part_1 / blink、donor_tripo_part_33 / browHappy|
|angry|42|block / block|donor_tripo_part_1 / mouthFrown、donor_tripo_part_1 / browAngry、donor_tripo_part_1 / blink|
|sad|35|block / none|donor_tripo_part_1 / browSad、donor_tripo_part_1 / blink、donor_tripo_part_33 / browSad|
|surprised|48|none / block|donor_tripo_part_1 / browSurprised、donor_tripo_part_33 / browSurprised、donor_tripo_part_34 / browSurprised|
|relaxed|41|block / block|donor_tripo_part_1 / browRelaxed、donor_tripo_part_1 / blink、donor_tripo_part_33 / browRelaxed|
|eyeSmile|30|block / none|donor_tripo_part_1 / RestoredSmile_CurrentGeometry、SharedNaturalBlush_Left / FollowFace_blink、SharedNaturalBlush_Right / FollowFace_blink|
|browHappy|9|none / none|donor_tripo_part_1 / browHappy、donor_tripo_part_33 / browHappy、donor_tripo_part_34 / browHappy|
|eyeCircleShock|33|block / none|donor_tripo_part_1 / blink、SharedNaturalBlush_Left / FollowFace_blink、SharedNaturalBlush_Right / FollowFace_blink|
|eyeTroubled|28|block / none|donor_tripo_part_1 / blink、SharedNaturalBlush_Left / FollowFace_blink、SharedNaturalBlush_Right / FollowFace_blink|
|eyeSpiral|31|block / none|donor_tripo_part_1 / blink、SharedNaturalBlush_Left / FollowFace_blink、SharedNaturalBlush_Right / FollowFace_blink|
|mouthOpenCircle|22|none / block|donor_tripo_part_1 / RestoredCircle_CurrentGeometry、FOUNDATION_LipThickness / RestoredCircle_CurrentGeometry、FOUNDATION_CartoonContour / RestoredCircle_CurrentGeometry|
|mouthSmile|6|none / none|donor_tripo_part_1 / NATIVE_mouthSmile、FOUNDATION_OralCavity / NATIVE_mouthSmile、FOUNDATION_LipThickness / NATIVE_mouthSmile|
|blush|2|none / none||
|cheekSlashes|2|none / none||
|shock|54|none / block|donor_tripo_part_1 / browSurprised、donor_tripo_part_33 / browSurprised、donor_tripo_part_34 / browSurprised|

## local-physics-reference

SHA-256 `733f30dca19ec899eb6cfd3f69210ce9d5b13be52cbfc73475a280ad69cf3fc6`

|键|绑定数|override|代表绑定|
|---|---:|---|---|
|aa|2|none / none|脸皮肤 / あ、牙舌口腔 / あ|
|blink|2|none / none|脸皮肤 / まばたき、牙舌口腔 / まばたき|
|happy|4|blend / none|脸皮肤 / にこり、脸皮肤 / 笑い、脸皮肤 / にやり|
|angry|4|none / none|脸皮肤 / Fcl_BRW_Angry、脸皮肤 / じと目、脸皮肤 / ∧|
|sad|4|none / none|脸皮肤 / 困る、脸皮肤 / Fcl_EYE_Sorrow、脸皮肤 / Fcl_MTH_Sorrow|
|surprised|3|none / none|脸皮肤 / 上、脸皮肤 / びっくり、牙舌口腔 / びっくり|
|relaxed|5|blend / none|脸皮肤 / にこり、脸皮肤 / Fcl_EYE_Fun、牙舌口腔 / Fcl_EYE_Fun|
|vee_Question22|1|none / none|脸皮肤 / vee_Question22|
|vee_Exclamation|1|none / none|脸皮肤 / vee_Exclamation|
|vee_Anger Mark|1|none / none|脸皮肤 / vee_Anger Mark|
