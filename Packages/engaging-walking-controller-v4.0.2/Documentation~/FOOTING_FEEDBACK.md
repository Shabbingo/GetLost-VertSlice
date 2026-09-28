# Hidden Footing Feedback

Version 3.3 adds optional non-diegetic feedback for the hidden footing meter.

## Automatic setup

Select the player and run:

`Tools > Walking Controller > Validate Selected Player`

The utility adds `FootingFeedback`, assigns the `WalkingMotor`, and uses the first child Camera as the feedback target. Existing settings are preserved.

## Feedback included

- Slow camera sway and roll as footing drops.
- A subtle screen-edge vignette that leaves the centre clear.
- A directional camera kick when footing drops quickly or a slide begins.
- Optional stumble/scuff audio clips.

The vignette is generated at runtime and does not require URP post-processing, a Volume, a Canvas, or an imported texture.

## Default thresholds

- Above `0.60` footing: no feedback.
- `0.60` to `0.15`: feedback smoothly increases.
- At or below `0.15`: full critical feedback.

## Recommended hierarchy

The camera itself can be assigned as `Camera Feedback Target`. The effect runs in `LateUpdate`, after the standard first-person look update. If another camera package overwrites camera rotation later than this component, create a child pivot for the Camera and assign that pivot instead.

## Audio

Assign a separate AudioSource and one or more short shoe-scuff or stumble clips. The AudioSource is optional. Keep spatial blend at zero for first-person feedback.

## Motion comfort

Reduce `Maximum Sway Roll`, `Stumble Roll`, and `Maximum Vignette Opacity` for a gentler effect. The entire camera and vignette sections can be disabled independently.


## Slip Body Drop (v4.0.2)

The directional stumble now uses an asymmetric body-drop envelope: a fast drop, optional short hold, and slower recovery. Position movement carries most of the effect, while yaw is deliberately avoided so the motion does not resemble weapon recoil or aim punch.

Suggested starting values:

```text
Slip Vertical Dip: 0.18
Slip Forward Dip: 0.07
Slip Lateral Shift: 0.025
Stumble Pitch: 9
Stumble Roll: 4.5
Slip Drop Time: 0.09
Slip Hold Time: 0.03
Slip Recovery Time: 0.45
Minor Stumble Strength: 0.35
```
