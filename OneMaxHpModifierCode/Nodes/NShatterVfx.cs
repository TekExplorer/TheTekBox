using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace OneMaxHpModifier.OneMaxHpModifierCode.Nodes;

public partial class NShatterVfx : Node
{
    public static bool ShowDebugBoundary = false;
    public static Color DebugBoundaryColor = Colors.Magenta;
    public static float PreShatterDelay = 0.0f;
    public static float SpeedModifier = 1.0f;

    private const int GridWidth = 6;
    private const int GridHeight = 6;
    private const int SparkCount = 14;

    private const float BaseExplosionForce = 320.0f;
    private const float BaseGravity = 1000.0f;
    private const float BaseShatterDuration = 0.95f;

    private CanvasItem _rootItem = null!;
    private Texture2D? _texture;
    private Material? _material;
    private Vector2 _actualVisualSize;
    private Vector2 _localOffset = Vector2.Zero;
    private Action? _onShatterStart;

    private readonly List<ShardFragment> _fragments = [];
    private readonly List<SparkParticle> _sparks = [];

    private float _shatterElapsedTime = 0.0f;
    private bool _isShattered = false;
    private float _scaledGravity;

    // Custom shard node that draws its texture slice with zero scaling or layout artifacts
    private partial class ShardNode : Control
    {
        public Texture2D Texture = null!;
        public Rect2 SourceRegion;
        public Vector2 TargetSize;

        public override void _Ready()
        {
            CustomMinimumSize = TargetSize;
            Size = TargetSize;
            PivotOffset = TargetSize / 2f;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            if (Texture == null) return;
            // Draws the source atlas region exactly mapped to destination (0, 0, TargetSize.X, TargetSize.Y)
            DrawTextureRectRegion(Texture, new Rect2(Vector2.Zero, TargetSize), SourceRegion);
        }
    }

    private record ShardFragment(
        ShardNode Node,
        Vector2 Velocity,
        Vector2 LocalOriginPosition
    );

    private record SparkParticle(
        ColorRect Rect,
        Vector2 Velocity,
        Vector2 Origin,
        float Drag
    );

    private static Node GetUiRoot(Node fallbackNode)
    {
        Control? uiTarget = NOverlayStack.Instance ?? (Control?)NRun.Instance?.GlobalUi;
        if (uiTarget != null && GodotObject.IsInstanceValid(uiTarget))
        {
            return uiTarget;
        }

        return fallbackNode.GetTree().Root;
    }

    /// <summary>
    /// Chests / World Nodes: Uses the live NRelic instance directly.
    /// </summary>
    public static void ShatterRelicNode(NRelic relicNode)
    {
        if (!GodotObject.IsInstanceValid(relicNode) || relicNode.Icon?.Texture == null)
            return;

        Vector2 localSize = relicNode.Icon.Size != Vector2.Zero
            ? relicNode.Icon.Size
            : relicNode.Icon.GetRect().Size;

        Vector2 iconLocalPos = relicNode.Icon.Position;

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
            _localOffset = iconLocalPos,
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

    /// <summary>
    /// UI Controls: Reward buttons, shop items, events.
    /// </summary>
    public static void ShatterTextureRect(TextureRect icon)
    {
        if (!GodotObject.IsInstanceValid(icon) || icon.Texture == null)
            return;

        // Use global canvas transform to account for parent offsets, pivots, and scaling
        Transform2D globalXform = icon.GetGlobalTransformWithCanvas();
        Vector2 screenPosition = globalXform.Origin;
        Vector2 renderedSize = icon.Size * globalXform.Scale;

        // If icon size hasn't settled, use texture size scaled by transform
        if (renderedSize == Vector2.Zero)
        {
            renderedSize = icon.Texture.GetSize() * globalXform.Scale;
        }

        icon.Visible = false;

        var container = new Control
        {
            Name = "ShatterProxy",
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
            _localOffset = Vector2.Zero
        };

        container.AddChild(vfx);

        Node uiRoot = GetUiRoot(icon);
        uiRoot.AddChildSafely(container);
    }

    public override void _Ready()
    {
        Callable.From(AssembleVisuals).CallDeferred();

        float effectiveSpeed = Mathf.Max(SpeedModifier, 0.01f);
        float activeShatterDuration = BaseShatterDuration / effectiveSpeed;
        float totalLifetime = PreShatterDelay + activeShatterDuration;

        Tween timeline = _rootItem.CreateTween();

        if (PreShatterDelay > 0.0f)
        {
            timeline.TweenInterval(PreShatterDelay);
        }

        timeline.TweenCallback(Callable.From(() =>
        {
            var debugBox = _rootItem.GetNodeOrNull<Control>("DebugBoundingBox");
            debugBox?.QueueFreeSafely();

            _isShattered = true;
        }));

        timeline.TweenInterval(activeShatterDuration);
        timeline.TweenCallback(Callable.From(() => _rootItem.QueueFreeSafely()));
    }

    private void AssembleVisuals()
    {
        if (_texture == null) return;

        _onShatterStart?.Invoke();
        _rootItem.Modulate = Colors.White;

        Vector2 texSize = _texture.GetSize();
        Vector2 renderSize = _actualVisualSize;
        Vector2 centerPoint = _localOffset + (renderSize / 2.0f);

        if (ShowDebugBoundary && _rootItem is Control rootControl)
        {
            var debugBox = new ReferenceRect
            {
                Name = "DebugBoundingBox",
                Size = renderSize,
                Position = _localOffset,
                BorderColor = DebugBoundaryColor,
                BorderWidth = 2.0f,
                EditorOnly = false
            };
            rootControl.AddChild(debugBox);
        }

        float cellW = renderSize.X / GridWidth;
        float cellH = renderSize.Y / GridHeight;

        float texCellW = texSize.X / GridWidth;
        float texCellH = texSize.Y / GridHeight;

        float explosionForce = BaseExplosionForce * (renderSize.Y / 100f);
        _scaledGravity = BaseGravity * (renderSize.Y / 100f);

        var rng = new RandomNumberGenerator();
        rng.Randomize();

        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                Vector2 localPos = _localOffset + new Vector2(x * cellW, y * cellH);
                Rect2 texRegion = new(x * texCellW, y * texCellH, texCellW, texCellH);

                var shardNode = new ShardNode
                {
                    Texture = _texture,
                    Material = _material,
                    SourceRegion = texRegion,
                    TargetSize = new Vector2(cellW, cellH),
                    Position = localPos
                };

                shardNode.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                _rootItem.AddChild(shardNode);

                Vector2 shardCenter = localPos + new Vector2(cellW / 2.0f, cellH / 2.0f);
                Vector2 dir = (shardCenter - centerPoint).Normalized();
                if (dir == Vector2.Zero)
                {
                    dir = Vector2.Up.Rotated(rng.RandfRange(-1.2f, 1.2f));
                }

                Vector2 velocity = dir * rng.RandfRange(0.75f, 1.45f) * explosionForce;
                _fragments.Add(new ShardFragment(shardNode, velocity, localPos));
            }
        }

        SpawnSparks(centerPoint, explosionForce * 1.3f, rng);

        if (PreShatterDelay <= 0.0f)
        {
            _isShattered = true;
        }
    }


    private void SpawnSparks(Vector2 center, float maxSpeed, RandomNumberGenerator rng)
    {
        for (int i = 0; i < SparkCount; i++)
        {
            float sparkSize = rng.RandfRange(3.0f, 5.5f);
            var spark = new ColorRect
            {
                Size = new Vector2(sparkSize, sparkSize),
                Position = center - Vector2.One * (sparkSize / 2f),
                PivotOffset = Vector2.One * (sparkSize / 2f),
                Color = new Color(1.5f, 1.3f, 0.8f, 1.0f),
                Rotation = rng.RandfRange(0, Mathf.Pi)
            };
            _rootItem.AddChild(spark);

            Vector2 dir = Vector2.Up.Rotated(rng.RandfRange(-Mathf.Pi, Mathf.Pi));
            Vector2 vel = dir * rng.RandfRange(0.5f, 1.0f) * maxSpeed;

            _sparks.Add(new SparkParticle(spark, vel, center, rng.RandfRange(2.5f, 4.0f)));
        }
    }

    public override void _Process(double delta)
    {
        if (!_isShattered) return;

        float effectiveSpeed = Mathf.Max(SpeedModifier, 0.01f);
        float dt = (float)delta * effectiveSpeed;
        _shatterElapsedTime += dt;

        float progress = Mathf.Clamp(_shatterElapsedTime / BaseShatterDuration, 0.0f, 1.0f);

        float shrinkProgress = Mathf.Clamp((progress - 0.15f) / 0.85f, 0.0f, 1.0f);
        float scaleFactor = 1.0f - Mathf.Ease(shrinkProgress, 1.8f);

        float alphaProgress = Mathf.Clamp((progress - 0.45f) / 0.55f, 0.0f, 1.0f);
        float alpha = 1.0f - Mathf.Ease(alphaProgress, 1.5f);

        foreach (var fragment in _fragments)
        {
            if (!GodotObject.IsInstanceValid(fragment.Node)) continue;

            Vector2 newPos = fragment.LocalOriginPosition
                           + (fragment.Velocity * _shatterElapsedTime)
                           + new Vector2(0, 0.5f * _scaledGravity * _shatterElapsedTime * _shatterElapsedTime);

            fragment.Node.Position = newPos;
            fragment.Node.Rotation += fragment.Velocity.X * 0.012f * dt;
            fragment.Node.Scale = Vector2.One * scaleFactor;

            Color mod = fragment.Node.Modulate;
            mod.A = alpha;
            fragment.Node.Modulate = mod;
        }

        foreach (var spark in _sparks)
        {
            if (!GodotObject.IsInstanceValid(spark.Rect)) continue;

            float dragFactor = (1.0f - Mathf.Exp(-spark.Drag * _shatterElapsedTime)) / spark.Drag;
            Vector2 newPos = spark.Origin + (spark.Velocity * dragFactor) + new Vector2(0, 80f * _shatterElapsedTime * _shatterElapsedTime);

            spark.Rect.Position = newPos;
            spark.Rect.Scale = Vector2.One * Mathf.Max(0.0f, 1.0f - (progress * 1.4f));

            Color c = spark.Rect.Color;
            c.A = Mathf.Max(0.0f, 1.0f - (progress * 1.2f));
            spark.Rect.Color = c;
        }
    }
}