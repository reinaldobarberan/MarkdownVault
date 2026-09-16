using MarkdownVault.Services;
using Xunit;

namespace MarkdownVault.Tests;

/// <summary>
/// Covers <see cref="GraphCamera"/>. The load-bearing property is the ROUND TRIP: unprojecting a
/// projected point has to give the original back. Dragging a node depends on it, and an inverse
/// that is subtly wrong makes nodes slide away from the cursor in a way that looks like a physics
/// bug and is nearly impossible to chase from the symptom.
/// </summary>
public class GraphCameraTests
{
    [Fact]
    public void Square_on_with_a_flat_layout_it_behaves_like_the_old_2D_camera()
    {
        // Flat mode must be a degenerate case of the same maths, not a second code path.
        var camera = new GraphCamera { Yaw = 0, Pitch = 0, Zoom = 0.85 };

        var p = camera.Project(200, -120, 0);

        Assert.Equal(0.85, p.Scale,  precision: 9);
        Assert.Equal(170,  p.X,      precision: 9);   // 200 * 0.85
        Assert.Equal(-102, p.Y,      precision: 9);   // -120 * 0.85
        Assert.Equal(camera.Distance, p.Depth, precision: 9);
    }

    [Fact]
    public void The_pivot_always_lands_in_the_middle_of_the_screen()
    {
        var camera = new GraphCamera { Yaw = 0.9, Pitch = -0.4, Zoom = 1.7 };

        var p = camera.Project(0, 0, 0);

        Assert.Equal(0, p.X, precision: 9);
        Assert.Equal(0, p.Y, precision: 9);
    }

    [Theory]
    [InlineData(0,     0,    1.0)]
    [InlineData(0.6,   0.35, 0.85)]
    [InlineData(-2.1,  1.3,  0.3)]
    [InlineData(3.0,  -1.4,  2.4)]
    public void Unprojecting_a_projected_point_gives_the_original_back(double yaw, double pitch, double zoom)
    {
        var camera = new GraphCamera { Yaw = yaw, Pitch = pitch, Zoom = zoom };
        int checkedPoints = 0;

        foreach (var (x, y, z) in new[]
        {
            (0.0, 0.0, 0.0), (350.0, -120.0, 90.0), (-40.0, 610.0, -300.0), (12.5, 7.25, 44.75)
        })
        {
            var p = camera.Project(x, y, z);

            // The round trip is only defined IN FRONT of the lens. Behind it the projection has
            // nothing meaningful to invert, and the camera's job is simply to say so — which the
            // renderer and Pick both rely on. At a steep angle with the camera pulled in close,
            // a far corner of the vault really does end up behind it.
            if (p.Depth <= GraphCamera.NearPlane) continue;

            var back = camera.Unproject(p.X, p.Y, p.Depth);

            Assert.Equal(x, back.X, precision: 6);
            Assert.Equal(y, back.Y, precision: 6);
            Assert.Equal(z, back.Z, precision: 6);
            checkedPoints++;
        }

        // Guard against the test quietly passing because every point was skipped.
        Assert.True(checkedPoints >= 2, $"solo se verificaron {checkedPoints} puntos, el caso quedó vacío");
    }

    [Fact]
    public void DepthOf_agrees_with_the_depth_the_projection_reports()
    {
        var camera = new GraphCamera { Yaw = 1.2, Pitch = 0.5, Zoom = 0.6 };

        var p = camera.Project(120, -80, 240);

        Assert.Equal(p.Depth, camera.DepthOf(120, -80, 240), precision: 9);
    }

    [Fact]
    public void What_is_nearer_draws_bigger()
    {
        // The whole point of perspective. Square-on, -Z is toward the camera.
        var camera = new GraphCamera { Yaw = 0, Pitch = 0, Zoom = 1 };

        var near = camera.Project(0, 0, -400);
        var far  = camera.Project(0, 0,  400);

        Assert.True(near.Depth < far.Depth);
        Assert.True(near.Scale > far.Scale);
    }

    [Fact]
    public void A_point_behind_the_camera_reports_a_depth_the_caller_can_reject()
    {
        // The renderer must be able to SEE that a node is behind the lens; a silently clamped
        // depth would draw it mirrored across the screen.
        var camera = new GraphCamera { Yaw = 0, Pitch = 0, Zoom = 1 };

        var p = camera.Project(0, 0, -camera.Distance - 500);

        Assert.True(p.Depth <= GraphCamera.NearPlane);
    }

    [Fact]
    public void Turning_the_camera_moves_what_is_on_screen()
    {
        // A point off the pivot must land somewhere else once the camera turns, or "orbit" would
        // be doing nothing at all.
        var still  = new GraphCamera { Yaw = 0,   Pitch = 0, Zoom = 1 };
        var turned = new GraphCamera { Yaw = 0.8, Pitch = 0, Zoom = 1 };

        var a = still.Project(300, 0, 0);
        var b = turned.Project(300, 0, 0);

        Assert.True(Math.Abs(a.X - b.X) > 1);
    }

    [Fact]
    public void Zooming_in_moves_the_camera_closer()
    {
        var wide  = new GraphCamera { Zoom = 0.5 };
        var close = new GraphCamera { Zoom = 2.0 };

        Assert.True(close.Distance < wide.Distance);
        Assert.Equal(GraphCamera.Focal / 2.0, close.Distance, precision: 9);
    }
}
