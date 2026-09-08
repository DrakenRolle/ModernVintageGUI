using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// A block, an item, a multiblock structure, an entity or any shape at all, shown as a 3D
    /// model the player can turn with the right mouse button and zoom with the wheel.
    ///
    /// It draws in both passes, the same split every control here makes:
    ///
    /// * The recessed frame is Cairo and lands in the dialog surface, so it costs nothing per
    ///   frame and is redrawn only when the dialog is.
    /// * The model is a mesh out of the block, item or entity atlas, drawn per frame in the
    ///   interactive pass, because a mesh cannot go into a Cairo surface any more than an item
    ///   stack can.
    ///
    /// Turning and zooming therefore cost **no redraw at all** - they change three floats that
    /// the per frame pass reads. That is why there is no Refresh() call anywhere in the input
    /// handlers, and it is not an oversight.
    ///
    /// Everything that can be shown ends up as one of two things: a mesh built once and kept
    /// (<see cref="ShowBlock"/>, <see cref="ShowItem"/>, <see cref="ShowBlocks(IEnumerable{ValueTuple{Vec3i, Block}})"/>,
    /// <see cref="ShowEntity(EntityProperties)"/>, <see cref="ShowShape(Shape, ITexPositionSource, Vec3f)"/>),
    /// or an entity that is alive in the world and is drawn by the game's own entity renderer
    /// (<see cref="ShowEntity(Entity)"/>), which is what the character creator does with the
    /// player. The first is a still; the second moves, wears its gear and is exactly what the
    /// player sees in the world.
    ///
    /// The model is drawn with the game's own <c>gui</c> shader, the one that is already bound
    /// when this pass runs and the one <c>RenderItemstackToGui</c> and <c>RenderEntityToGui</c>
    /// draw with. Nothing about the GL state is touched: the stage arrives with the depth test,
    /// the depth mask and back face culling on, which is precisely what a turning model needs,
    /// and a block in an inventory slot is drawn under the same state with the same shader. An
    /// earlier version swapped in the standard world shader and toggled the depth state around
    /// the draw, and both of those were where its bugs came from.
    /// </summary>
    public class ShapeViewerControl : UIControl, IDisposable
    {
        #region Styling
        /// <summary>Corner radius of the recessed panel, in author units.</summary>
        private const double UnscaledFrameRadius = 3.0;

        /// <summary>How thick the panel is outlined.</summary>
        private const double UnscaledFrameWidth = 2.0;

        /// <summary>
        /// Whether the viewer draws its own recessed panel. Off for a viewer that sits on
        /// something that already provides a background.
        /// </summary>
        public bool DrawsFrame { get; set; } = true;

        /// <summary>
        /// The ground the model stands on.
        ///
        /// Deliberately *not* GuiStyle.DialogSlotBackColor, which is what a slot uses: that is
        /// a light peach, and it is the right colour behind a small bright item icon. A model
        /// is a large object lit flatly from every side, and on a light ground its own light
        /// faces disappear into it. A dark recess is what every model viewer uses, for this
        /// reason.
        /// </summary>
        public ElementColor BackgroundColor { get; set; } = new ElementColor(0.10, 0.08, 0.07, 0.85);
        #endregion

        #region View state
        /// <summary>The angle <see cref="ResetView"/> puts the model back to.</summary>
        public float DefaultYaw { get; set; } = 45f;

        /// <summary>The tilt <see cref="ResetView"/> puts the model back to.</summary>
        public float DefaultPitch { get; set; } = 22.5f;

        /// <summary>
        /// Rotation about the vertical axis, in degrees. Positive turns the model to the right,
        /// so its front travels the way a right drag travels. Right drag changes it.
        /// </summary>
        public float Yaw { get; set; } = 45f;

        /// <summary>
        /// Tilt about the horizontal axis, in degrees. Positive tips the top of the model
        /// towards the viewer, so a larger pitch looks down on it from higher up. Clamped to
        /// <see cref="MaxPitch"/> so the model cannot be turned past its own poles and come out
        /// upside down.
        /// </summary>
        public float Pitch { get; set; } = 22.5f;

        /// <summary>How far the model may be tipped, in degrees.</summary>
        public float MaxPitch { get; set; } = 89f;

        /// <summary>A multiplier on the fitted size. The wheel changes it.</summary>
        public float Zoom { get; set; } = 1f;

        public float MinZoom { get; set; } = 0.3f;
        public float MaxZoom { get; set; } = 6f;

        /// <summary>What one wheel tick multiplies or divides the zoom by.</summary>
        public float ZoomStep { get; set; } = 1.15f;

        /// <summary>
        /// How much of the frame the model fills at zoom 1, as a fraction of the shorter side.
        ///
        /// The model is fitted by its bounding sphere, not its box: a cube turned corner on is
        /// √3 wider than its edge, and fitting the box would have it poking out of the frame at
        /// some angles and not others. Fitting the sphere means no angle clips, at the price of
        /// a cube looking a little small when seen face on. A larger frame simply shows a
        /// larger model - which is what a caller who made the frame larger wanted.
        /// </summary>
        public double ModelFill { get; set; } = 0.9;

        /// <summary>
        /// A fixed size instead of <see cref="ModelFill"/>: the diameter of the model's bounding
        /// sphere at zoom 1, in author units. Null, the default, fits the frame.
        /// </summary>
        public double? UnscaledModelSize { get; set; }

        /// <summary>Degrees of rotation per pixel of drag.</summary>
        public double RotateSensitivity { get; set; } = 0.6;

        /// <summary>
        /// Which button turns the model. Right by default, as asked for - and because the left
        /// one is what a dialog uses for everything else, so leaving it free means the viewer
        /// can still sit inside something that wants to be clicked.
        /// </summary>
        public EnumMouseButton RotateButton { get; set; } = EnumMouseButton.Right;

        /// <summary>Whether the wheel zooms. Off makes the viewer transparent to a scrolling list.</summary>
        public bool WheelZooms { get; set; } = true;

        /// <summary>
        /// Whether an entity handed to <see cref="ShowEntity(Entity)"/> is drawn live by the
        /// game's entity renderer - animated, dressed, exactly as in the world - or tesselated
        /// once into a still like everything else. Live is the default; a still is what you
        /// get anyway for an entity the client has no renderer for.
        /// </summary>
        public bool LiveEntityRendering { get; set; } = true;

        /// <summary>Puts the view back where it started.</summary>
        public void ResetView()
        {
            Yaw = DefaultYaw;
            Pitch = DefaultPitch;
            Zoom = 1f;
        }
        #endregion

        #region Model
        /// <summary>
        /// Builds the mesh for whatever was last shown, on the first frame that has a client
        /// API to build it with.
        ///
        /// A delegate rather than a stored block or item, so that every kind of input is the
        /// same thing to the render pass and adding a new kind is one more Show method. It is
        /// deferred so a tree can be assembled before the dialog exists and so the layout
        /// harness, which has no API at all, can lay this control out like any other.
        /// </summary>
        private System.Func<ICoreClientAPI, MeshData?>? _buildMesh;

        /// <summary>An entity alive in the world, drawn by its own renderer instead of a mesh.</summary>
        private Entity? _liveEntity;

        private MultiTextureMeshRef? _mesh;

        /// <summary>Middle of the mesh's bounding box, in model units. The model turns about it.</summary>
        private Vec3f _centre = new Vec3f(0.5f, 0.5f, 0.5f);

        /// <summary>
        /// Half the diagonal of the mesh's bounding box, in model units - the radius of the
        /// sphere the model fits in whichever way it is turned. Everything about sizing comes
        /// from this one number.
        /// </summary>
        private float _radius = 0.87f;

        /// <summary>
        /// Set once the tesselation, the upload or the draw threw. A GPU failure repeated every
        /// frame is a frozen client and a log file the size of a disk, so it is tried once and
        /// then left alone until the model is changed.
        /// </summary>
        private bool _renderFailed;

        private bool _disposed;

        /// <summary>Whether there is something to look at.</summary>
        public bool HasModel => _buildMesh != null || _liveEntity != null;

        /// <summary>Show nothing.</summary>
        public void Clear()
        {
            SetSource(null, null);
        }

        /// <summary>Show a block, as it looks placed in the world.</summary>
        public void ShowBlock(Block? block)
        {
            if (block == null)
            {
                Clear();
                return;
            }

            SetSource(api =>
            {
                api.Tesselator.TesselateBlock(block, out MeshData data);
                return data;
            });
        }

        /// <summary>Show an item. A flat item icon becomes the thin voxel slab the game uses for it.</summary>
        public void ShowItem(Item? item)
        {
            if (item == null)
            {
                Clear();
                return;
            }

            SetSource(api =>
            {
                api.Tesselator.TesselateItem(item, out MeshData data);
                return data;
            });
        }

        /// <summary>Show whatever a stack holds, block or item.</summary>
        public void ShowStack(ItemStack? stack)
        {
            if (stack?.Block != null)
            {
                ShowBlock(stack.Block);
            }
            else
            {
                ShowItem(stack?.Item);
            }
        }

        /// <summary>
        /// Show several blocks as one structure - a multiblock, a machine, a build.
        ///
        /// Offsets are in blocks and may start anywhere: the structure is centred on its own
        /// bounds, so (0,0,0)-(1,0,1) and (100,50,100)-(101,50,101) come out the same. Air is
        /// skipped. Each block is tesselated as it would be in the world and the meshes are
        /// merged into one, which is why a structure of forty blocks costs no more per frame
        /// than a single one.
        /// </summary>
        public void ShowBlocks(IEnumerable<(Vec3i Offset, Block Block)>? blocks)
        {
            if (blocks == null)
            {
                Clear();
                return;
            }

            // Copied now rather than enumerated on the first frame: the caller's sequence may
            // be a live query over something that has changed by then.
            var placed = new List<(Vec3i Offset, Block Block)>();

            foreach ((Vec3i offset, Block block) in blocks)
            {
                if (block == null || block.BlockId == 0 || offset == null)
                    continue;

                placed.Add((offset, block));
            }

            if (placed.Count == 0)
            {
                Clear();
                return;
            }

            SetSource(api => BuildStructure(api, placed));
        }

        /// <summary>
        /// Show the blocks standing at these positions in the world - a multiblock's members,
        /// say. The blocks are read now, so what is shown is what stood there at the call.
        /// </summary>
        public void ShowBlocks(IEnumerable<BlockPos>? positions, IBlockAccessor? blockAccessor)
        {
            if (positions == null || blockAccessor == null)
            {
                Clear();
                return;
            }

            var placed = new List<(Vec3i Offset, Block Block)>();

            foreach (BlockPos pos in positions)
            {
                if (pos == null)
                    continue;

                Block? block = blockAccessor.GetBlock(pos);

                if (block == null || block.BlockId == 0)
                    continue;

                placed.Add((new Vec3i(pos.X, pos.Y, pos.Z), block));
            }

            // Brought back to the origin. The bounds would centre the model wherever it stood,
            // but a mesh is floats, and at world coordinates in the hundreds of thousands a
            // float has a few centimetres of precision left - visibly jagged faces.
            if (placed.Count > 0)
            {
                int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;

                foreach ((Vec3i offset, _) in placed)
                {
                    minX = Math.Min(minX, offset.X);
                    minY = Math.Min(minY, offset.Y);
                    minZ = Math.Min(minZ, offset.Z);
                }

                for (int i = 0; i < placed.Count; i++)
                {
                    Vec3i offset = placed[i].Offset;

                    placed[i] = (
                        new Vec3i(offset.X - minX, offset.Y - minY, offset.Z - minZ),
                        placed[i].Block);
                }
            }

            ShowBlocks(placed);
        }

        /// <summary>
        /// Show a schematic. Its block codes are looked up in the given world, so a schematic
        /// saved with a mod that is no longer loaded simply has holes where those blocks were.
        /// </summary>
        public void ShowSchematic(BlockSchematic? schematic, IWorldAccessor? world)
        {
            if (schematic == null || world == null)
            {
                Clear();
                return;
            }

            var placed = new List<(Vec3i Offset, Block Block)>();
            int count = Math.Min(schematic.Indices.Count, schematic.BlockIds.Count);

            for (int i = 0; i < count; i++)
            {
                // The packing BlockSchematic.Pack uses: x in the low ten bits, z in the next
                // ten, y in the ten above that.
                uint index = schematic.Indices[i];
                int x = (int)(index & 0x3FF);
                int z = (int)((index >> 10) & 0x3FF);
                int y = (int)((index >> 20) & 0x3FF);

                if (!schematic.BlockCodes.TryGetValue(schematic.BlockIds[i], out AssetLocation? code))
                    continue;

                Block? block = world.GetBlock(code);

                if (block == null || block.BlockId == 0)
                    continue;

                placed.Add((new Vec3i(x, y, z), block));
            }

            ShowBlocks(placed);
        }

        /// <summary>
        /// Show an entity type - a creature that is not in the world, or not near. Its shape
        /// is tesselated once in the rest pose with the textures the client baked for it at
        /// start up, so this needs no entity to exist anywhere.
        /// </summary>
        public void ShowEntity(EntityProperties? type)
        {
            if (type?.Client == null)
            {
                Clear();
                return;
            }

            EntityClientProperties client = type.Client;

            SetSource(api =>
            {
                var textures = new EntityTypeTextureSource(api.EntityTextureAtlas, client.Textures);
                return BuildEntityMesh(api, type, client.LoadedShape, textures);
            });
        }

        /// <summary>
        /// Show an entity that is alive in the world - the player, the creature being looked
        /// at. With <see cref="LiveEntityRendering"/> on and a renderer to hand it is drawn
        /// live, the way the character creator draws the player: animated, wearing its gear.
        /// Otherwise it is tesselated once into a still, with its own textures.
        /// </summary>
        public void ShowEntity(Entity? entity)
        {
            if (entity?.Properties?.Client == null)
            {
                Clear();
                return;
            }

            EntityProperties type = entity.Properties;

            SetSource(
                api =>
                {
                    ITexPositionSource textures = api.Tesselator.GetTextureSource(entity);
                    Shape? shape = type.Client.LoadedShapeForEntity ?? type.Client.LoadedShape;

                    return BuildEntityMesh(api, type, shape, textures);
                },
                LiveEntityRendering ? entity : null);
        }

        /// <summary>
        /// Show any shape at all with any textures at all. The texture source answers the
        /// shape's texture codes; <c>capi.Tesselator.GetTextureSource(block)</c> is one way to
        /// get one, and a class of your own is another.
        /// </summary>
        /// <param name="rotationDeg">Turns the mesh once, before it is shown, the way a
        /// composite shape's rotateX/Y/Z does.</param>
        public void ShowShape(Shape? shape, ITexPositionSource? textures, Vec3f? rotationDeg = null)
        {
            if (shape == null || textures == null)
            {
                Clear();
                return;
            }

            SetSource(api =>
            {
                api.Tesselator.TesselateShape(
                    "mvgui-shapeviewer",
                    shape,
                    out MeshData data,
                    textures,
                    rotationDeg);

                return data;
            });
        }

        /// <summary>
        /// Show a shape from its asset location, loaded on the first frame. The location may
        /// be given the way a JSON file gives it - <c>game:block/wood/crate</c> - or in full,
        /// with the <c>shapes/</c> prefix and the <c>.json</c> suffix.
        /// </summary>
        public void ShowShape(AssetLocation? shapeLocation, ITexPositionSource? textures, Vec3f? rotationDeg = null)
        {
            if (shapeLocation == null || textures == null)
            {
                Clear();
                return;
            }

            // Cloned because the two With methods change the location they are called on, and
            // the caller's copy is not ours to change.
            AssetLocation path = shapeLocation.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");

            SetSource(api =>
            {
                Shape? shape = Shape.TryGet(api, path);

                if (shape == null)
                {
                    api.Logger.Warning(
                        "[ModernVintageGUI] ShapeViewerControl '{0}': no shape at {1}",
                        Name,
                        path);

                    return null;
                }

                api.Tesselator.TesselateShape(
                    "mvgui-shapeviewer",
                    shape,
                    out MeshData data,
                    textures,
                    rotationDeg);

                return data;
            });
        }

        /// <summary>
        /// Show a mesh you built yourself. It is uploaded as it is; the vertex positions are
        /// read for the bounds, so it can be any size and sit anywhere.
        /// </summary>
        public void ShowMesh(MeshData? mesh)
        {
            if (mesh == null)
            {
                Clear();
                return;
            }

            SetSource(api => mesh);
        }

        /// <summary>
        /// The escape hatch every other Show method is built on: a function that turns a client
        /// API into a mesh. It runs once, on the first frame, and the result is kept until the
        /// model changes.
        /// </summary>
        public void ShowCustom(System.Func<ICoreClientAPI, MeshData?>? buildMesh)
        {
            SetSource(buildMesh);
        }

        private void SetSource(System.Func<ICoreClientAPI, MeshData?>? buildMesh, Entity? liveEntity = null)
        {
            _buildMesh = buildMesh;
            _liveEntity = liveEntity;
            ReleaseMesh();
        }

        private void ReleaseMesh()
        {
            _mesh?.Dispose();
            _mesh = null;
            _renderFailed = false;
        }

        /// <summary>
        /// Every block in the structure, tesselated as it would be in the world and merged into
        /// one mesh at its offset. AddMeshData carries the atlas page of every face across, so
        /// a structure out of blocks on different pages draws correctly in one call.
        /// </summary>
        private static MeshData? BuildStructure(ICoreClientAPI api, List<(Vec3i Offset, Block Block)> placed)
        {
            MeshData? merged = null;

            foreach ((Vec3i offset, Block block) in placed)
            {
                api.Tesselator.TesselateBlock(block, out MeshData part);

                if (part == null || part.VerticesCount == 0)
                    continue;

                if (merged == null)
                {
                    // Cloned rather than used as the accumulator: whether the tesselator hands
                    // out a fresh mesh or one it keeps is its business, and translating a kept
                    // one would move that block in every later use.
                    merged = part.Clone();
                    merged.Translate(offset.X, offset.Y, offset.Z);
                }
                else
                {
                    merged.AddMeshData(part, offset.X, offset.Y, offset.Z);
                }
            }

            return merged;
        }

        /// <summary>
        /// An entity's shape in its rest pose. The composite shape's rotation is applied the
        /// way the entity renderer applies it, so a creature modelled lying on its side is
        /// shown standing up.
        /// </summary>
        private MeshData? BuildEntityMesh(ICoreClientAPI api, EntityProperties type, Shape? shape, ITexPositionSource textures)
        {
            EntityClientProperties client = type.Client;

            if (shape == null && client.Shape?.Base != null)
            {
                AssetLocation path = client.Shape.Base.Clone()
                    .WithPathPrefixOnce("shapes/")
                    .WithPathAppendixOnce(".json");

                shape = Shape.TryGet(api, path);
            }

            if (shape == null)
            {
                api.Logger.Warning(
                    "[ModernVintageGUI] ShapeViewerControl '{0}': entity {1} has no shape to show",
                    Name,
                    type.Code);

                return null;
            }

            api.Tesselator.TesselateShape(
                "mvgui-shapeviewer-entity",
                shape,
                out MeshData data,
                textures,
                client.Shape?.RotateXYZCopy);

            return data;
        }

        /// <summary>
        /// Builds and uploads the mesh on first use, and reads its bounds while the vertices
        /// are still in reach.
        ///
        /// Tesselating and uploading are both expensive and neither depends on the frame, so
        /// they happen once and the result is kept until the model changes or the control is
        /// disposed. The dialog owns that disposal - see CustomDialogElement.DisposeChildren,
        /// which walks the tree and disposes anything that can be.
        ///
        /// The multi texture path rather than a single bound atlas: a block's faces can come
        /// from more than one atlas page, and binding only the first one draws the rest with
        /// the wrong pixels. RenderMultiTextureMesh binds per group and gets that right.
        /// </summary>
        private void EnsureMesh(ICoreClientAPI api)
        {
            if (_mesh != null || _renderFailed || _buildMesh == null)
                return;

            try
            {
                MeshData? data = _buildMesh(api);

                if (data == null || data.VerticesCount == 0)
                {
                    // Nothing to draw is not an error - a schematic of air, an entity with no
                    // shape - but it is not worth trying again every frame either.
                    _renderFailed = true;
                    return;
                }

                MeasureBounds(data);

                _mesh = api.Render.UploadMultiTextureMesh(data);
            }
            catch (Exception ex)
            {
                _renderFailed = true;

                api.Logger.Warning(
                    "[ModernVintageGUI] ShapeViewerControl '{0}' could not build its mesh: {1}",
                    Name,
                    ex.Message);
            }
        }

        /// <summary>
        /// The bounding box of the vertices, reduced to a centre and a radius.
        ///
        /// This is what lets one control show a pebble, a pulverizer and a three block tall
        /// creature at the same apparent size: nothing here assumes the unit cube a block is
        /// built in. A block with a shape larger than its cube - most multiblock controllers -
        /// gets its whole shape fitted, not the cube.
        /// </summary>
        private void MeasureBounds(MeshData data)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            float[] xyz = data.xyz;
            int count = data.VerticesCount * 3;

            for (int i = 0; i + 2 < count; i += 3)
            {
                float x = xyz[i], y = xyz[i + 1], z = xyz[i + 2];

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            _centre = new Vec3f((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);

            float dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            float radius = 0.5f * MathF.Sqrt(dx * dx + dy * dy + dz * dz);

            // A single flat quad has a radius; a single vertex does not, and dividing by it
            // would blow the model up to infinity.
            _radius = radius > 0.0001f ? radius : 0.5f;
        }
        #endregion

        #region Construction
        public ShapeViewerControl(string _Name = "")
            : base(_Name)
        {
            // A viewer has no natural size - it is a window onto something, and how big that
            // window is is the caller's decision, not the model's.
            Size = new PointD(120, 120);
            IsAutoSize = false;

            // It takes the keyboard so that a dialog can tab to it; the arrow keys turn it.
            IsFocusable = true;

            MouseDown += OnMouseDownHere;
            MouseMove += OnMouseMoveHere;
            MouseUp += OnMouseUpHere;
            MouseWheel += OnMouseWheelHere;
            KeyDown += OnKeyDownHere;
        }
        #endregion

        #region Cairo pass - the frame
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            if (DrawsFrame)
            {
                double radius = UnscaledFrameRadius * LayoutScale;
                double line = UnscaledFrameWidth * LayoutScale;

                GuiElement.RoundRectangle(ctx, Position.X, Position.Y, Size.X, Size.Y, radius);
                ctx.SetSourceRGBA(
                    BackgroundColor.RNormalized,
                    BackgroundColor.GNormalized,
                    BackgroundColor.BNormalized,
                    BackgroundColor.ANormalized);
                ctx.Fill();

                // The warm inner line a slot has, so the viewer reads as part of the same
                // furniture rather than as a hole cut in the dialog.
                GuiElement.RoundRectangle(ctx, Position.X, Position.Y, Size.X, Size.Y, radius);
                ctx.SetSourceRGBA(GuiStyle.DialogSlotFrontColor);
                ctx.LineWidth = line;
                ctx.Stroke();

                // And the dark outer line, which is what makes it look sunk in rather than laid
                // on. Vanilla's slot ends with the same stroke.
                GuiElement.RoundRectangle(
                    ctx,
                    Position.X + line / 2,
                    Position.Y + line / 2,
                    Size.X - line,
                    Size.Y - line,
                    radius);
                ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.8);
                ctx.LineWidth = line / 2;
                ctx.Stroke();
            }

            base.GenerateRenderData(surface, ctx);
        }
        #endregion

        #region Interactive pass - the model
        /// <summary>
        /// How much depth the model is given in front of the dialog surface, in the GUI's z
        /// units.
        ///
        /// The ortho stage tests depth, and a larger z is nearer - see
        /// <see cref="CustomDialogElement.SurfaceRenderZ"/>. The model has depth of its own, so
        /// it is put a little in front of the surface and squashed along z to fit in this band
        /// whenever it would reach outside it. Squashing z changes nothing the eye can see - the
        /// projection is orthographic - and it keeps even a large, zoomed in model behind the
        /// stack on the cursor, which the dialog draws at
        /// <see cref="CustomDialogElement.HeldItemZOffset"/>.
        /// </summary>
        public const float ModelDepthBand = 300f;

        /// <summary>
        /// Below this alpha a texel is not drawn. The item renderer's usual value; without it
        /// the transparent margin of a leaf texture would write depth and hide the leaf behind.
        /// </summary>
        private const float AlphaTest = 0.05f;

        private static readonly Vec4f White = new Vec4f(1f, 1f, 1f, 1f);
        private static readonly Vec4f NoGlow = new Vec4f(0f, 0f, 0f, 0f);

        // Three matrices, reused: allocating them per frame would be garbage for nothing.
        private readonly Matrixf _placement = new Matrixf();
        private readonly Matrixf _model = new Matrixf();
        private readonly Matrixf _modelView = new Matrixf();

        /// <summary>The diameter of the model's bounding sphere on screen, in device pixels.</summary>
        private double ModelDiameterPixels()
        {
            double diameter = UnscaledModelSize.HasValue
                ? UnscaledModelSize.Value * LayoutScale
                : ModelFill * Math.Min(Size.X, Size.Y);

            return diameter * Zoom;
        }

        /// <summary>
        /// Draws the model, clipped to the frame.
        ///
        /// The shader is the game's <c>gui</c> program, which this stage runs with. The uniforms
        /// set here are the ones <c>RenderItemstackToGui</c> sets for a block in a slot, and
        /// they are put back the way that method puts them back, so what follows in the stage
        /// sees the state it expects. The matrices are the same pair it uses: the model matrix
        /// for the normals, so the lighting turns with the model, and the model view matrix -
        /// the stage's current one times ours - for the vertices.
        ///
        /// The model's own Y axis points up; the GUI's points down. The two are reconciled with
        /// a half turn about X rather than a mirror, because the stage culls back faces and a
        /// mirror reverses the winding of every face - the model would be drawn inside out.
        /// </summary>
        public override void GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)
        {
            base.GenerateInteractiveRenderData(api, deltaTime);

            if (_disposed || !IsVisible || _renderFailed || !HasModel)
                return;

            // Clip to the inside of the frame. A zoomed in model runs past the edge, and
            // without this it would run on over the neighbouring controls.
            LayoutRect? clip = FrameClip();

            if (clip == null)
                return;

            IRenderAPI rapi = api.Render;

            PointD screen = GetScreenPosition();
            double centreX = screen.X + Size.X / 2.0;
            double centreY = screen.Y + Size.Y / 2.0;

            float surfaceZ = Dialog?.SurfaceRenderZ ?? 0f;
            float depth = surfaceZ + CustomDialogElement.SlotItemZOffset + ModelDepthBand / 2f;

            ApplyScissor(api, clip.Value);

            try
            {
                if (_liveEntity != null && _liveEntity.Properties?.Client?.Renderer != null)
                {
                    RenderLiveEntity(rapi, deltaTime, _liveEntity, centreX, centreY, depth);
                }
                else
                {
                    EnsureMesh(api);

                    if (_mesh != null)
                    {
                        RenderMesh(rapi, centreX, centreY, depth);
                    }
                }
            }
            catch (Exception ex)
            {
                _renderFailed = true;

                // Once. The guard at the top of the method is what keeps it to once.
                api.Logger.Warning(
                    "[ModernVintageGUI] ShapeViewerControl '{0}' failed to draw and was switched off: {1}",
                    Name,
                    ex.Message);
            }
            finally
            {
                RestoreAncestorScissor(api);
            }
        }

        /// <summary>
        /// The inside of the frame, cut down to whatever clips this control, in dialog local
        /// coordinates. Null when nothing of it is visible.
        /// </summary>
        private LayoutRect? FrameClip()
        {
            double inset = DrawsFrame ? UnscaledFrameWidth * LayoutScale : 0;

            var frame = new LayoutRect(
                Position.X + inset,
                Position.Y + inset,
                Size.X - inset * 2,
                Size.Y - inset * 2);

            LayoutRect? outer = EffectiveClip();
            LayoutRect clip = outer.HasValue ? frame.Intersect(outer.Value) : frame;

            return clip.IsEmpty ? (LayoutRect?)null : clip;
        }

        private void RenderMesh(IRenderAPI rapi, double centreX, double centreY, float depth)
        {
            IShaderProgram? prog = rapi.CurrentActiveShader;

            // The uniforms below belong to the gui program. If the stage is ever run with
            // something else bound, setting them would fail quietly and draw garbage; better
            // to draw nothing.
            if (prog == null || prog.PassName != "gui")
                return;

            float pixelsPerUnit = (float)(ModelDiameterPixels() / (2.0 * _radius));

            // How far the model reaches along z once turned, and how much to squash it so it
            // stays inside the depth band. One at any ordinary size; less only when zoomed in.
            float reach = _radius * pixelsPerUnit;
            float squash = reach > ModelDepthBand / 2f ? ModelDepthBand / 2f / reach : 1f;

            // Read in the order they apply to a vertex, which is bottom up: move the model's
            // centre to the origin, spin it about its vertical, tip it about the horizontal,
            // turn its Y up into the GUI's Y down, and scale to pixels.
            _placement.Identity()
                .Scale(pixelsPerUnit, pixelsPerUnit, pixelsPerUnit)
                .RotateXDeg(180f)
                .RotateXDeg(-Pitch)
                .RotateYDeg(-Yaw)
                .Translate(-_centre.X, -_centre.Y, -_centre.Z);

            // For the normals: the placement at the frame centre, nothing else. Squashing z is
            // kept out of it because a non uniform scale bends normals and the lighting with
            // them.
            _model.Identity()
                .Translate(centreX, centreY, depth)
                .Mul(_placement);

            // For the vertices: the stage's own model view first, then the placement, with the
            // squash in between so it acts on the finished picture.
            _modelView.Set(rapi.CurrentModelviewMatrix)
                .Translate(centreX, centreY, depth)
                .Scale(1f, 1f, squash)
                .Mul(_placement);

            prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
            prog.UniformMatrix("modelMatrix", _model.Values);
            prog.UniformMatrix("modelViewMatrix", _modelView.Values);
            prog.Uniform("applyModelMat", 1);
            prog.Uniform("applyAnimation", 0);
            prog.Uniform("normalShaded", 1);
            prog.Uniform("rgbaIn", White);
            prog.Uniform("rgbaGlowIn", NoGlow);
            prog.Uniform("extraGlow", 0);
            prog.Uniform("tempGlowMode", 0);
            prog.Uniform("noTexture", 0f);
            prog.Uniform("overlayOpacity", 0f);
            prog.Uniform("damageEffect", 0f);
            prog.Uniform("alphaTest", AlphaTest);

            // "tex2d" is the sampler's name in gui.fsh. Each group of the mesh binds its own
            // atlas page to it before drawing.
            rapi.RenderMultiTextureMesh(_mesh, "tex2d", 0);

            // Back to what the item renderer leaves behind, which is what the rest of the
            // stage is written against.
            prog.Uniform("applyModelMat", 0);
            prog.Uniform("normalShaded", 0);
            prog.Uniform("alphaTest", 0f);
            prog.Uniform("rgbaGlowIn", NoGlow);
        }

        /// <summary>
        /// Draws an entity through the game's own renderer, the way the character creator
        /// draws the player.
        ///
        /// RenderEntityToGui places the entity itself: its feet land at
        /// <c>(posX + size, posY + 2 * size)</c> and one world unit is <c>size</c> times the
        /// type's own size in pixels, with the entity standing upright on the screen. Those two
        /// facts are read off EntityShapeRenderer.loadModelMatrixForGui, and the numbers below
        /// invert them so the entity's box is centred in the frame at the fitted size. The tilt
        /// goes through the GL matrix stack about the frame centre, which the renderer picks
        /// up as its view matrix - the same trick the character creator plays with its
        /// GlRotate(-14) - and the spin goes in as the renderer's own yaw, in radians and with
        /// the opposite sign because it turns the other way.
        /// </summary>
        private void RenderLiveEntity(IRenderAPI rapi, float deltaTime, Entity entity, double centreX, double centreY, float depth)
        {
            Cuboidf? box = entity.SelectionBox ?? entity.CollisionBox;

            float width = box?.XSize ?? 1f;
            float height = box?.YSize ?? 1f;
            float length = box?.ZSize ?? 1f;

            float radius = 0.5f * MathF.Sqrt(width * width + height * height + length * length);
            if (radius < 0.0001f)
                radius = 0.5f;

            float pixelsPerUnit = (float)(ModelDiameterPixels() / (2.0 * radius));
            float typeSize = Math.Max(0.0001f, entity.Properties.Client.Size);
            float size = pixelsPerUnit / typeSize;

            double feetY = centreY + height * pixelsPerUnit / 2.0;
            double posX = centreX - size;
            double posY = feetY - 2.0 * size;

            float reach = radius * pixelsPerUnit;
            float squash = reach > ModelDepthBand / 2f ? ModelDepthBand / 2f / reach : 1f;

            rapi.GlPushMatrix();

            try
            {
                rapi.GlTranslate(centreX, centreY, depth);
                rapi.GlScale(1f, 1f, squash);
                rapi.GlRotate(-Pitch, 1f, 0f, 0f);
                rapi.GlTranslate(-centreX, -centreY, -depth);

                rapi.RenderEntityToGui(
                    deltaTime,
                    entity,
                    posX,
                    posY,
                    depth,
                    -Yaw * GameMath.DEG2RAD,
                    size,
                    ColorUtil.WhiteArgb);
            }
            finally
            {
                rapi.GlPopMatrix();
            }
        }
        #endregion

        #region Input
        /// <summary>Where the cursor was on the last move, for working out the drag delta.</summary>
        private int _lastX;
        private int _lastY;
        private bool _isTurning;

        /// <summary>
        /// A viewer is one thing to the mouse, not a box with parts in it.
        /// </summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        private void OnMouseDownHere(object? sender, MouseEventArgs e)
        {
            if (e.Button != RotateButton || !HasModel)
                return;

            _isTurning = true;
            _lastX = e.X;
            _lastY = e.Y;

            // Without capture the drag ends the moment the cursor leaves the frame, which for a
            // viewer is immediately - turning a model is exactly the gesture that runs off the
            // edge of the control it started in. PixelCanvasControl takes the same capture for
            // the same reason.
            Dialog?.CaptureMouse(this);

            e.Handled = true;
        }

        private void OnMouseMoveHere(object? sender, MouseEventArgs e)
        {
            if (!_isTurning)
                return;

            // The delta is worked out from the last position rather than read off the event.
            // MouseEvent carries a DeltaX, but it is the platform's idea of the movement and is
            // not filled in on every path that reaches us - two positions we saw ourselves
            // always agree with what the cursor actually did.
            Turn(e.X - _lastX, e.Y - _lastY);

            _lastX = e.X;
            _lastY = e.Y;

            // No Refresh(): the rotation is read by the per frame pass, so it is already on
            // screen next frame. Asking for a redraw here would rebuild the whole dialog surface
            // for every mouse move of a drag, which is the most expensive thing this framework
            // can do and would buy nothing.
            e.Handled = true;
        }

        private void OnMouseUpHere(object? sender, MouseEventArgs e)
        {
            if (!_isTurning)
                return;

            _isTurning = false;
            Dialog?.ReleaseMouseCapture();

            e.Handled = true;
        }

        private void OnMouseWheelHere(object? sender, IS2Mod.ControlTypes.Events.MouseWheelEventArgs e)
        {
            if (!WheelZooms || e.IsHandled || !HasModel)
                return;

            ZoomBy(e.delta);

            // Consumed, or it carries on to the ancestors - and a viewer inside a scrolling list
            // would zoom and scroll the list at the same time.
            e.SetHandled();
        }

        private void OnKeyDownHere(object? sender, KeyEventArgs e)
        {
            if (!HasModel)
                return;

            // A tenth of a turn per press, which is coarse enough to get round the model without
            // holding a key down for a minute.
            const float Step = 15f;

            // In degrees, not pixels: a key press is not a gesture on the screen, so it must not
            // be divided by the GUI scale the way a drag is.
            switch (e.Key)
            {
                case GlKeys.Left: TurnBy(-Step, 0); break;
                case GlKeys.Right: TurnBy(Step, 0); break;
                case GlKeys.Up: TurnBy(0, -Step); break;
                case GlKeys.Down: TurnBy(0, Step); break;
                default: return;
            }

            // Only the four keys that did something are consumed. Anything else stays with the
            // dialog and then with the game, which is the rule the whole keyboard story here
            // rests on - see CustomDialogElement.HandleKeyDown.
            e.Handled = true;
        }

        /// <summary>Applies a drag, measured in screen pixels, to the rotation.</summary>
        private void Turn(int dx, int dy)
        {
            // Divided by the GUI scale so that dragging the same distance across the screen
            // turns the model by the same amount whatever the scale is. The gesture is measured
            // in device pixels; the rotation it means is not.
            double scale = Math.Max(0.0001, LayoutScale);

            // Right takes the front of the model to the right; down brings its top towards the
            // viewer. Both are the model following the hand, which is the gesture every
            // viewer teaches.
            TurnBy(
                (float)(dx * RotateSensitivity / scale),
                (float)(dy * RotateSensitivity / scale));
        }

        /// <summary>Turns the model by an angle, in degrees.</summary>
        public void TurnBy(float yawDegrees, float pitchDegrees)
        {
            Yaw += yawDegrees;
            Pitch += pitchDegrees;

            // Yaw wraps - a model can be turned round and round. Pitch does not: past the pole
            // the model comes out upside down and the drag direction reverses, which reads as
            // the control breaking rather than as a feature.
            Yaw %= 360f;
            Pitch = Math.Clamp(Pitch, -MaxPitch, MaxPitch);
        }

        /// <summary>One wheel tick, in or out.</summary>
        private void ZoomBy(int ticks)
        {
            float factor = (float)Math.Pow(ZoomStep, ticks);

            Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        }
        #endregion

        #region Entity type textures
        /// <summary>
        /// Answers a shape's texture codes out of an entity type's textures, the way the game's
        /// own texture source does for a live entity - but from the type alone, so no entity
        /// has to exist. The client bakes every entity type's textures into the entity atlas
        /// at start up, which is what makes the sub ids valid here.
        /// </summary>
        private sealed class EntityTypeTextureSource : ITexPositionSource
        {
            private readonly ITextureAtlasAPI _atlas;
            private readonly IDictionary<string, CompositeTexture>? _textures;

            public EntityTypeTextureSource(ITextureAtlasAPI atlas, IDictionary<string, CompositeTexture>? textures)
            {
                _atlas = atlas;
                _textures = textures;
            }

            public Size2i? AtlasSize => _atlas.Size;

            public TextureAtlasPosition? this[string textureCode]
            {
                get
                {
                    CompositeTexture? texture = null;

                    if (_textures != null
                        && !_textures.TryGetValue(textureCode, out texture))
                    {
                        // The game's fallback: a type with one texture calls it "all".
                        _textures.TryGetValue("all", out texture);
                    }

                    BakedCompositeTexture? baked = texture?.Baked;

                    if (baked != null)
                    {
                        TextureAtlasPosition[] positions = _atlas.Positions;

                        if (baked.TextureSubId >= 0 && baked.TextureSubId < positions.Length)
                            return positions[baked.TextureSubId];
                    }

                    return _atlas.UnknownTexturePosition;
                }
            }
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _buildMesh = null;
            _liveEntity = null;
            ReleaseMesh();

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
