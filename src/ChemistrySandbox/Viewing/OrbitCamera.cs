using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Input;

namespace ChemistrySandbox.Viewing;

/// <summary>
/// A camera circling a point on the arena: right-drag orbits, middle-drag or WASD pans, Q and E turn,
/// the wheel zooms. It also turns a cursor position into a world ray for picking.
/// </summary>
internal sealed class OrbitCamera : IDisposable
{
    private const double FieldOfView = 55, Near = 0.1, Far = 500;
    private const float OrbitDegreesPerScreen = 240f, PanPerSecond = 24f, TurnDegreesPerSecond = 90f;
    private const float ZoomPerWheelStep = 0.9f, WheelStep = 100f;
    private const float MinPitch = -88f, MaxPitch = -8f, MinDistance = 8f, MaxDistance = 160f;

    private readonly IEngineContext engine;
    private readonly Camera camera;
    private readonly Vector3 home;
    private Vector3 target;
    private float yaw = 35f, pitch = -42f, distance = 70f;
    private Vector2? cursor;

    internal OrbitCamera(IEngineContext engine, Vector3 home)
    {
        this.engine = engine;
        this.home = home;
        target = home;
        camera = engine.CameraView.CreateCamera(Descriptor());
        engine.CameraView.SetActiveCamera(camera);
    }

    /// <summary>The cursor on the canvas, normalized and bottom-left based, once the pointer has moved over it.</summary>
    internal Vector2? Cursor => cursor;

    internal void Reset()
    {
        target = home;
        (yaw, pitch, distance) = (35f, -42f, 70f);
        engine.CameraView.UpdateCamera(new(camera, Descriptor()));
    }

    /// <summary>Applies one update's pointer and keyboard input; <paramref name="seconds"/> scales key motion.</summary>
    internal void Control(ReadOnlySpan<ProductInputEvent> input, PhysicalInputState held, float seconds)
    {
        bool moved = false;
        foreach (ProductInputEvent fact in input)
        {
            if (fact.Kind == InputEventKind.PointerPosition || (fact.Kind == InputEventKind.PointerButton && fact.HasPosition))
            {
                Vector2 next = new(fact.X, fact.Y);
                if (cursor is Vector2 previous && fact.Kind == InputEventKind.PointerPosition)
                {
                    Vector2 delta = next - previous;
                    if (held.Held(PointerButton.Secondary))
                    {
                        yaw -= delta.X * OrbitDegreesPerScreen;
                        pitch = Math.Clamp(pitch - (delta.Y * OrbitDegreesPerScreen * 0.5f), MinPitch, MaxPitch);
                        moved = true;
                    }
                    else if (held.Held(PointerButton.Middle))
                    {
                        Pan(new Vector2(-delta.X, -delta.Y) * distance);
                        moved = true;
                    }
                }

                cursor = next;
            }
            else if (fact.Kind == InputEventKind.Wheel && fact.Y != 0f)
            {
                distance = Math.Clamp(distance * MathF.Pow(ZoomPerWheelStep, -fact.Y / WheelStep), MinDistance, MaxDistance);
                moved = true;
            }
        }

        Vector2 keys = new((held.Held(KeyboardControl.KeyD) ? 1 : 0) - (held.Held(KeyboardControl.KeyA) ? 1 : 0),
            (held.Held(KeyboardControl.KeyW) ? 1 : 0) - (held.Held(KeyboardControl.KeyS) ? 1 : 0));
        if (keys != Vector2.Zero)
        {
            Pan(keys * PanPerSecond * seconds);
            moved = true;
        }

        float turn = (held.Held(KeyboardControl.KeyE) ? 1 : 0) - (held.Held(KeyboardControl.KeyQ) ? 1 : 0);
        if (turn != 0f)
        {
            yaw -= turn * TurnDegreesPerSecond * seconds;
            moved = true;
        }

        if (moved)
        {
            engine.CameraView.UpdateCamera(new(camera, Descriptor()));
        }
    }

    /// <summary>The world ray through a point on the canvas, normalized and bottom-left based.</summary>
    internal CameraRay Ray(Vector2 point)
    {
        CameraSurfaceReadout surface = engine.CameraView.ReadSurface();
        double aspect = surface.Reported && surface.CssHeight > 0 ? surface.CssWidth / surface.CssHeight : 16.0 / 9.0;
        return CameraQueries.Ray(Descriptor(), aspect, point);
    }

    public void Dispose() => camera.Dispose();

    /// <summary>Moves the target across the ground: x to the screen's right, y away from the viewer.</summary>
    private void Pan(Vector2 amount)
    {
        float radians = yaw * MathF.PI / 180f;
        Vector3 forward = new(-MathF.Sin(radians), 0f, -MathF.Cos(radians));
        Vector3 right = new(-forward.Z, 0f, forward.X);
        target += (right * amount.X) + (forward * amount.Y);
    }

    private CameraDescriptor Descriptor()
    {
        float yawRadians = yaw * MathF.PI / 180f, pitchRadians = pitch * MathF.PI / 180f;
        Vector3 back = new(MathF.Sin(yawRadians) * MathF.Cos(pitchRadians), -MathF.Sin(pitchRadians), MathF.Cos(yawRadians) * MathF.Cos(pitchRadians));
        CameraQueries.TryLookAtPose(target + (back * distance), target, 0, out CameraPose pose);
        return new(pose, CameraBasisMode.Derived, default, new(CameraProjectionKind.Perspective, FieldOfView, 0, Near, Far),
            CameraViewports.Full);
    }
}
