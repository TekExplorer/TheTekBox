using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace OneMaxHpModifier.OneMaxHpModifierCode.Nodes;

public partial class NShatterVfx : Node
{
    // ==========================================
    // DEBUG CONTROLS
    // ==========================================
    public static bool EnableDebugFreeze = true;
    public static float DebugFreezeDuration = 2.0f;

    public static bool EnableSlowMotion = true;
    public static float SlowMotionTimeScale = 0.2f;
    // ==========================================

    private const int GridWidth = 6;
    private const int GridHeight = 6;
    private const float BaseExplosionForce = 280.0f;
    private const float BaseGravity = 980.0f;
    private const float BaseShatterDuration = 1.1f;

    private CanvasItem _rootItem = null!;
    private Texture2D? _texture;
    private Material? _material;
    private Vector2 _actualVisualSize;
    private Vector2 _localOffset = Vector2.Zero; // <--- Tracks icon position relative to parent
    private Action? _onShatterStart;

    private readonly List<ShardFragment> _fragments = [];
    private float _shatterElapsedTime = 0.0f;
    private bool _isShattered = false;
    private float _scaledGravity;

    private record ShardFragment(Control Rect, Vector2 Velocity, Vector2 LocalOriginPosition);

    private static Node GetUiRoot(Node fallbackNode)
    {
        Control? uiTarget = NOverlayStack.Instance ?? (Control?)NRun.Instance?.GlobalUi;
        if (uiTarget != null && GodotObject.IsInstanceValid(uiTarget))
        {
            return uiTarget;
        }

        return fallbackNode.GetTree().Root;
    }

    public static void ShatterRelicNode(NRelic relicNode)
    {
        if (!GodotObject.IsInstanceValid(relicNode) || relicNode.Icon?.Texture == null)
            return;

        Vector2 localSize = relicNode.Icon.Size != Vector2.Zero
            ? relicNode.Icon.Size
            : relicNode.Icon.GetRect().Size;

        Vector2 iconLocalPos = relicNode.Icon.Position; // Capture local offset

        Node uiRoot = GetUiRoot(relicNode);
        relicNode.Reparent(uiRoot);
        relicNode.ZIndex = 4096;
        relicNode.Visible = true;
        relicNode.Modulate = Colors.White;

        if (GodotObject.IsInstanceValid(relicNode.Outline))
        {
            relicNode.Outline.Visible = false;
        }

        var vfx = new NShatterVfx
        {
            _rootItem = relicNode,
            _texture = relicNode.Icon.Texture,
            _material = relicNode.Icon.Material,
            _actualVisualSize = localSize,
            _localOffset = iconLocalPos, // Pass offset
            _onShatterStart = () =>
            {
                if (GodotObject.IsInstanceValid(relicNode.Icon))
                {
                    relicNode.Icon.Visible = false;
                }
            }
        };

        relicNode.AddChild(vfx);
    }

    public static void ShatterTextureRect(TextureRect icon)
    {
        if (!GodotObject.IsInstanceValid(icon) || icon.Texture == null)
            return;

        Rect2 globalRect = icon.GetGlobalRect();
        Vector2 renderedSize = globalRect.Size;
        Vector2 screenPosition = globalRect.Position;

        icon.Visible = false;

        var container = new Control
        {
            Name = "ShatterRewardProxy",
            TopLevel = true,
            ZIndex = 4096,
            Position = screenPosition,
            Size = renderedSize,
            CustomMinimumSize = renderedSize,
            PivotOffset = renderedSize / 2f
        };

        var vfx = new NShatterVfx
        {
            _rootItem = container,
            _texture = icon.Texture,
            _material = icon.Material,
            _actualVisualSize = renderedSize,
            _localOffset = Vector2.Zero // Proxy has no child offset
        };

        container.AddChild(vfx);

        Node uiRoot = GetUiRoot(icon);
        uiRoot.AddChildSafely(container);
    }

    public override void _Ready()
    {
        Callable.From(AssembleShards).CallDeferred();

        float delayBeforeMotion = EnableDebugFreeze ? DebugFreezeDuration : 0.0f;
        float totalLifetime = delayBeforeMotion + (EnableSlowMotion ? (BaseShatterDuration / SlowMotionTimeScale) : BaseShatterDuration);

        Tween timeline = _rootItem.CreateTween();
        if (delayBeforeMotion > 0.0f)
        {
            timeline.TweenInterval(delayBeforeMotion);
        }

        timeline.TweenCallback(Callable.From(() =>
        {
            var debugBox = _rootItem.GetNodeOrNull<Control>("DebugBoundingBox");
            debugBox?.QueueFreeSafely();

            _isShattered = true;
        }));

        timeline.TweenInterval(totalLifetime - delayBeforeMotion);
        timeline.TweenCallback(Callable.From(() => _rootItem.QueueFreeSafely()));
    }

    private void AssembleShards()
    {
        if (_texture == null) return;

        _onShatterStart?.Invoke();

        Vector2 texSize = _texture.GetSize();
        Vector2 renderSize = _actualVisualSize;

        if (EnableDebugFreeze && _rootItem is Control rootControl)
        {
            var debugBox = new ReferenceRect
            {
                Name = "DebugBoundingBox",
                Size = renderSize,
                Position = _localOffset, // Offset the debug box too
                BorderColor = Colors.Magenta,
                BorderWidth = 2.0f,
                EditorOnly = false
            };
            rootControl.AddChild(debugBox);
        }

        float cellW = renderSize.X / GridWidth;
        float cellH = renderSize.Y / GridHeight;
        Vector2 centerPoint = _localOffset + (renderSize / 2.0f); // Center relative to offset

        float texCellW = texSize.X / GridWidth;
        float texCellH = texSize.Y / GridHeight;

        Vector2 shardScale = new(cellW / texCellW, cellH / texCellH);

        float explosionForce = BaseExplosionForce * (renderSize.Y / 100f);
        _scaledGravity = BaseGravity * (renderSize.Y / 100f);

        var rng = new RandomNumberGenerator();
        rng.Randomize();

        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                // Add _localOffset to position shards where the Icon actually was
                Vector2 shardScreenPos = _localOffset + new Vector2(x * cellW, y * cellH);
                Vector2 shardTexRegionPos = new(x * texCellW, y * texCellH);

                AtlasTexture atlas = new()
                {
                    Atlas = _texture,
                    Region = new Rect2(shardTexRegionPos, new Vector2(texCellW, texCellH))
                };

                TextureRect shardRect = new()
                {
                    Texture = atlas,
                    Material = _material,
                    Position = shardScreenPos,
                    Size = new Vector2(texCellW, texCellH),
                    Scale = shardScale,
                    PivotOffset = Vector2.Zero,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Visible = true
                };

                shardRect.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                _rootItem.AddChild(shardRect);

                Vector2 shardCenter = shardScreenPos + new Vector2(cellW / 2.0f, cellH / 2.0f);
                Vector2 dir = (shardCenter - centerPoint).Normalized();
                if (dir == Vector2.Zero)
                {
                    dir = Vector2.Up.Rotated(rng.RandfRange(-1.2f, 1.2f));
                }

                Vector2 velocity = dir * rng.RandfRange(0.7f, 1.4f) * explosionForce;
                _fragments.Add(new ShardFragment(shardRect, velocity, shardScreenPos));
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_isShattered) return;

        float timeMultiplier = EnableSlowMotion ? SlowMotionTimeScale : 1.0f;
        float dt = (float)delta * timeMultiplier;
        _shatterElapsedTime += dt;

        foreach (var fragment in _fragments)
        {
            if (!GodotObject.IsInstanceValid(fragment.Rect)) continue;

            Vector2 newPos = fragment.LocalOriginPosition
                           + (fragment.Velocity * _shatterElapsedTime)
                           + new Vector2(0, 0.5f * _scaledGravity * _shatterElapsedTime * _shatterElapsedTime);

            fragment.Rect.Position = newPos;
            fragment.Rect.Rotation += fragment.Velocity.X * 0.012f * dt;
        }
    }
}