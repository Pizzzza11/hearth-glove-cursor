# 图像生成提示词

模式：Codex 内置图像生成工具。

```text
Use case: stylized-concept
Asset type: Windows mouse cursor animation source sprite
Primary request: create an original fantasy tavern golden leather glove mouse cursor inspired by the two reference poses, with a normal pointing pose and a pressed/click pose.
Input images: Image 1 is the normal-pose composition reference; Image 2 is the pressed-pose composition reference. Use them only for pose and readability, not for pixel copying.
Subject: one chunky warm-gold leather glove with a broad cuff at bottom-right and index finger aiming precisely to the top-left; the pressed pose bends the index finger downward by a clearly visible amount while the rest of the glove stays consistent.
Style/medium: polished painterly fantasy game UI icon, slightly chunky forms, warm leather, subtle brass highlights, dark brown outline and soft amber rim light; crisp at small size.
Composition/framing: a 2-column by 1-row sprite sheet on a square transparent canvas; normal pose centered in the left half, pressed pose centered in the right half; both the same scale, same lighting, same orientation, and same silhouette except the index finger movement; generous transparent padding; top-left fingertip in each cell is the intended hotspot.
Constraints: actual transparent background; no text; no logo; no card, rune, spiral, watermark, border, panel, drop shadow outside the hand; do not crop either glove; exactly two glove figures total.
```

## 第二版提亮提示词

```text
Use case: precise-object-edit
Asset type: Windows mouse cursor two-pose sprite sheet
Primary request: make both existing golden leather gloves slightly lighter and whiter, changing the material from saturated yellow-gold to pale ivory-gold leather with soft champagne highlights.
Input images: Image 1 is the edit target.
Constraints: change only the color and highlights; preserve the exact two-column layout, each hand's pose, anatomy, proportions, scale, orientation, painterly fantasy-game rendering, crisp dark outline, transparent background, spacing, and all edges. Keep the left normal pointing pose and the right bent-finger pressed pose unchanged. Exactly two gloves. No rotation in this edit; rotation will be applied programmatically. No text, logo, rune, watermark, border, background, or extra objects.
```

说明：提亮版生成结果用于确定浅象牙金配色；为了保留原图的真实透明边缘，最终光标在构建脚本中对透明原图执行等效浅化，并对按下帧精确旋转 10°。
