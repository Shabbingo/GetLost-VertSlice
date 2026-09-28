# Hidden Footing

WalkingMotor now tracks an internal `CurrentFooting` value from 1 (secure) to 0 (lost footing). It is intentionally not displayed to players.

Footing is reduced by:

- sprinting on slopes;
- moving downhill;
- sudden changes of direction;
- low surface traction;
- a surface's Footing Loss Multiplier.

Footing recovers while standing, moving on safe ground, or using careful walk. When footing reaches the Slip Footing Threshold on a slope above Minimum Footing Slip Angle, an uncontrolled slide begins.

## Recommended starting values

- Use Hidden Footing: enabled
- Minimum Footing Slip Angle: 14
- Footing Risk Start Angle: 8
- Footing Loss Rate: 0.38
- Footing Recovery Rate: 0.55
- Sprint Footing Loss: 1
- Downhill Footing Loss: 0.85
- Turning Footing Loss: 0.65
- Careful Footing Recovery Multiplier: 2.25
- Slip Footing Threshold: 0.08

## Surface examples

Trail: Footing Loss Multiplier 0.25
Wet grass: 1.4
Mud: 1.6
Loose scree: 2.2
Rock: 0.8 dry, higher when wet

`CurrentFooting` and `FootingDanger` are public read-only properties for future animation, controller vibration, breathing, or accessibility feedback.
