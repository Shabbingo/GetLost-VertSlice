# Upgrading the Engaging Walking Controller

## From v1.0 or v1.1 to v1.2

The package keeps the same package name, assembly name, namespace, class names and serialized field names. Existing player components, Input Action references, surface profiles and tuned values are retained.

### Recommended upgrade process

1. Commit or back up the Unity project.
2. Open **Window > Package Manager**.
3. Remove the old local/tarball version of **Engaging Walking Controller**.
4. Click **+ > Add package from tarball...** and choose the new `.tgz`.
5. Allow Unity to recompile.
6. Select the player and press **Validate and Repair References** on `WalkingMotor`, or use **Tools > Walking Controller > Validate All Players In Open Scenes**.

Removing the package entry does not remove components or serialized values from your scenes and prefabs, provided the replacement package is installed before you save those assets with missing scripts.

## What the validator changes

The validator only fills references that are currently empty. It does not replace assigned objects or reset gameplay values.

It can repair references to:

- CharacterController
- WalkingInputReader
- GroundProbe
- TerrainSurfaceResolver
- ExertionController
- A child Camera transform

It also reports missing Move/Look actions and required components.

## Compatibility policy for future versions

Future releases will retain:

- Package ID: `com.tom.walking-controller`
- Runtime assembly: `Tom.WalkingController`
- Namespace: `Tom.WalkingController`
- Existing public class names
- Existing serialized field names

When a serialized field must be renamed, the package will use Unity's `FormerlySerializedAs` migration attribute.


## 3.0.0 to 3.1.0

No component replacement is required. Existing `WalkingMotor` components receive a new serialized **Sprint Slide Increase** field with a default value of `0.5`. Set it to `0` to retain the old sprint behaviour.
