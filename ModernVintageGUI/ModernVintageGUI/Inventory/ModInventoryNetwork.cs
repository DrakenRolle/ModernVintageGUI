using ProtoBuf;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ModernVintageGUI.Inventory
{
    /// <summary>Where an inventory a dialog wants to open comes from.</summary>
    public enum ModInventoryKind
    {
        /// <summary>A block entity's own, addressed by its position.</summary>
        Block,

        /// <summary>One the mod owns under a name, which anything may open.</summary>
        Shared,

        /// <summary>One the mod owns per player, addressed by a class name.</summary>
        Player
    }

    /// <summary>Client to server: a dialog showing this inventory was opened or closed.</summary>
    [ProtoContract]
    public class ModInventoryToggle
    {
        [ProtoMember(1)]
        public ModInventoryKind Kind;

        /// <summary>A position as "x/y/z", a shared name, or a player inventory class name.</summary>
        [ProtoMember(2)]
        public string Key = "";

        [ProtoMember(3)]
        public bool Opened;
    }

    internal static class ModInventoryChannel
    {
        public const string Name = "modernvintagegui";

        public static IClientNetworkChannel Client(ICoreClientAPI capi)
        {
            return capi.Network.GetChannel(Name)
                ?? capi.Network.RegisterChannel(Name).RegisterMessageType<ModInventoryToggle>();
        }

        public static IServerNetworkChannel Server(ICoreServerAPI sapi)
        {
            return sapi.Network.GetChannel(Name)
                ?? sapi.Network.RegisterChannel(Name).RegisterMessageType<ModInventoryToggle>();
        }
    }

    /// <summary>
    /// The server half. One of these per mod, created in StartServerSide.
    ///
    /// What it does is small and it is the whole game: when a client says it opened a dialog, it
    /// finds the inventory that dialog means and calls
    /// <c>player.InventoryManager.OpenInventory(inventory)</c>.
    ///
    /// That one call is what makes an inventory real. ServerSystemInventory resolves every slot
    /// move by inventory id through the player's inventory manager, so an inventory that is not
    /// in there does not exist as far as the server is concerned and every move for it is
    /// dropped. OpenInventory puts it in there and opens it, and from that moment the server
    /// syncs its dirty slots to that client by itself, exactly as it does for the hotbar.
    /// Everything else follows from the same list: shift clicking moves stacks into it, and
    /// taking an item from the creative inventory into it works, because both resolve their
    /// target the same way.
    ///
    /// Sizes are registered here and never taken from the client. A client that could name a
    /// size could ask for an inventory of any size it liked.
    /// </summary>
    public sealed class ModInventorySystem
    {
        /// <summary>How far a player may be from a block and still open its inventory.</summary>
        private const double MaxBlockReach = 12.0;

        private sealed class PlayerEntry
        {
            public ModInventory Inventory = null!;
            public string PlayerUid = "";
            public string ClassName = "";
        }

        private readonly ICoreServerAPI _sapi;

        private readonly Dictionary<string, int> _playerSizes = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _sharedSizes = new Dictionary<string, int>();

        private readonly Dictionary<string, PlayerEntry> _playerInventories = new Dictionary<string, PlayerEntry>();
        private readonly Dictionary<string, ModInventory> _sharedInventories = new Dictionary<string, ModInventory>();

        public ModInventorySystem(ICoreServerAPI sapi)
        {
            _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));

            ModInventoryChannel.Server(sapi).SetMessageHandler<ModInventoryToggle>(OnToggle);

            sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
            sapi.Event.GameWorldSave += SaveAll;
        }

        #region Registration
        /// <summary>
        /// Declares an inventory every player has one of - a personal stash, a loadout. It is
        /// created when the player first opens it and saved with that player.
        /// </summary>
        public void RegisterPlayerInventory(string className, int size)
        {
            _playerSizes[Require(className, nameof(className))] = Math.Max(1, size);
        }

        /// <summary>
        /// Declares one inventory the whole server shares under this name. Any number of blocks
        /// or dialogs can open it, and they all see the same contents - the server holds one
        /// instance and syncs it to everyone who has it open.
        /// </summary>
        public void RegisterSharedInventory(string name, int size)
        {
            _sharedSizes[Require(name, nameof(name))] = Math.Max(1, size);
        }

        private static string Require(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("A mod inventory needs a name", parameter);

            return value;
        }

        /// <summary>The shared inventory under this name, for server code that wants to fill it.</summary>
        public ModInventory? GetShared(string name)
        {
            return _sharedSizes.ContainsKey(name) ? LoadShared(name) : null;
        }

        /// <summary>This player's copy of a per player inventory.</summary>
        public ModInventory? GetForPlayer(IServerPlayer player, string className)
        {
            return _playerSizes.TryGetValue(className, out int size)
                ? LoadForPlayer(player, className, size).Inventory
                : null;
        }
        #endregion

        #region Open / close
        private void OnToggle(IServerPlayer player, ModInventoryToggle message)
        {
            if (message?.Key == null)
                return;

            ModInventory? inventory = Resolve(player, message);

            if (inventory == null)
                return;

            if (message.Opened)
            {
                player.InventoryManager.OpenInventory(inventory);

                // Nothing has changed since it was loaded, so the dirty slot sync would send
                // nothing and the client would show an empty grid. Push every slot once - the
                // empty ones included, so a slot the client still remembers is cleared.
                for (int i = 0; i < inventory.Count; i++)
                {
                    inventory.MarkSlotDirty(i);
                }
            }
            else
            {
                player.InventoryManager.CloseInventory(inventory);
                Save(message.Kind, message.Key, player, inventory);
            }
        }

        private ModInventory? Resolve(IServerPlayer player, ModInventoryToggle message)
        {
            switch (message.Kind)
            {
                case ModInventoryKind.Block:
                    return ResolveBlock(player, message.Key);

                case ModInventoryKind.Shared:
                    return _sharedSizes.ContainsKey(message.Key) ? LoadShared(message.Key) : Unknown(player, message);

                case ModInventoryKind.Player:
                    return _playerSizes.TryGetValue(message.Key, out int size)
                        ? LoadForPlayer(player, message.Key, size).Inventory
                        : Unknown(player, message);

                default:
                    return null;
            }
        }

        private ModInventory? Unknown(IServerPlayer player, ModInventoryToggle message)
        {
            _sapi.Logger.Notification(
                "[ModernVintageGUI] {0} asked to open the unknown {1} inventory '{2}'.",
                player.PlayerName, message.Kind, message.Key);

            return null;
        }

        /// <summary>
        /// The inventory of the block entity at this position, if it has one and the player is
        /// close enough to be plausibly standing at it.
        ///
        /// The distance check is the reason this does not simply trust the position: the message
        /// comes from a client, and without it a client could open the inventory of any block
        /// anywhere on the map by sending its coordinates.
        /// </summary>
        private ModInventory? ResolveBlock(IServerPlayer player, string key)
        {
            BlockPos? pos = ParsePos(key);

            if (pos == null)
                return null;

            if (player.Entity == null || player.Entity.Pos.DistanceTo(pos.ToVec3d().Add(0.5, 0.5, 0.5)) > MaxBlockReach)
            {
                _sapi.Logger.Notification(
                    "[ModernVintageGUI] {0} asked to open the block inventory at {1} from too far away.",
                    player.PlayerName, pos);

                return null;
            }

            var holder = _sapi.World.BlockAccessor.GetBlockEntity(pos) as IModInventoryHolder;

            return holder?.GetModInventory();
        }

        internal static string PosKey(BlockPos pos)
        {
            return pos.X + "/" + pos.Y + "/" + pos.Z;
        }

        private static BlockPos? ParsePos(string key)
        {
            string[] parts = key.Split('/');

            if (parts.Length != 3
                || !int.TryParse(parts[0], out int x)
                || !int.TryParse(parts[1], out int y)
                || !int.TryParse(parts[2], out int z))
            {
                return null;
            }

            return new BlockPos(x, y, z);
        }
        #endregion

        #region Contents
        private PlayerEntry LoadForPlayer(IServerPlayer player, string className, int size)
        {
            string id = ModInventoryAccess.PlayerInventoryId(className, player.PlayerUID);

            if (_playerInventories.TryGetValue(id, out PlayerEntry? existing))
                return existing;

            var inventory = new ModInventory(size, id, _sapi);
            Restore(inventory, player.GetModdata(ModdataKey(className)), id);

            var entry = new PlayerEntry
            {
                Inventory = inventory,
                PlayerUid = player.PlayerUID,
                ClassName = className
            };

            _playerInventories[id] = entry;
            return entry;
        }

        private ModInventory LoadShared(string name)
        {
            string id = ModInventoryAccess.SharedInventoryId(name);

            if (_sharedInventories.TryGetValue(id, out ModInventory? existing))
                return existing;

            var inventory = new ModInventory(_sharedSizes[name], id, _sapi);
            Restore(inventory, _sapi.WorldManager.SaveGame.GetData(ModdataKey(name)), id);

            _sharedInventories[id] = inventory;
            return inventory;
        }

        private void Restore(ModInventory inventory, byte[]? stored, string id)
        {
            if (stored == null || stored.Length == 0)
                return;

            try
            {
                inventory.FromTreeAttributes(TreeAttribute.CreateFromBytes(stored));
                inventory.ResolveBlocksOrItems();
            }
            catch (Exception e)
            {
                // A block or item that no longer exists, or data from an older slot count.
                // Losing the contents is bad; refusing to open the dialog at all is worse.
                _sapi.Logger.Warning(
                    "[ModernVintageGUI] Could not read the stored inventory '{0}', starting empty: {1}",
                    id, e);
            }
        }

        private void Save(ModInventoryKind kind, string key, IServerPlayer player, ModInventory inventory)
        {
            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);

            switch (kind)
            {
                case ModInventoryKind.Player:
                    player.SetModdata(ModdataKey(key), tree.ToBytes());
                    break;

                case ModInventoryKind.Shared:
                    _sapi.WorldManager.SaveGame.StoreData(ModdataKey(key), tree.ToBytes());
                    break;

                // A block inventory is saved with its chunk by BlockEntityContainer - saving it
                // again here would be a second, older copy of the same thing.
                case ModInventoryKind.Block:
                    break;
            }
        }

        private void SaveAll()
        {
            foreach (PlayerEntry entry in _playerInventories.Values)
            {
                if (_sapi.World.PlayerByUid(entry.PlayerUid) is IServerPlayer player)
                {
                    Save(ModInventoryKind.Player, entry.ClassName, player, entry.Inventory);
                }
            }

            foreach (KeyValuePair<string, int> shared in _sharedSizes)
            {
                if (_sharedInventories.TryGetValue(ModInventoryAccess.SharedInventoryId(shared.Key), out ModInventory? inventory))
                {
                    var tree = new TreeAttribute();
                    inventory.ToTreeAttributes(tree);
                    _sapi.WorldManager.SaveGame.StoreData(ModdataKey(shared.Key), tree.ToBytes());
                }
            }
        }

        private void OnPlayerDisconnect(IServerPlayer player)
        {
            var gone = new List<string>();

            foreach (KeyValuePair<string, PlayerEntry> pair in _playerInventories)
            {
                if (pair.Value.PlayerUid != player.PlayerUID)
                    continue;

                Save(ModInventoryKind.Player, pair.Value.ClassName, player, pair.Value.Inventory);
                gone.Add(pair.Key);
            }

            // Dropped rather than kept: the contents are on the player now, and holding the
            // object would leak one inventory per player per session. Shared ones stay - they
            // belong to the world, not to whoever happened to open them.
            foreach (string id in gone)
            {
                _playerInventories.Remove(id);
            }
        }

        private static string ModdataKey(string name)
        {
            return "modernvintagegui-" + name;
        }
        #endregion
    }

    /// <summary>
    /// The client half: the inventory this client works with, and the two messages that tell the
    /// server when the dialog holding it opens and closes.
    ///
    /// Hand one to <see cref="IS2Mod.ControlTypes.InventoryGridControl.SetInventory(ModInventoryAccess)"/>
    /// and the grid takes care of the rest - the packets a slot move produces, and opening and
    /// closing along with the dialog.
    ///
    /// Both sides address the inventory by the same id, and that is the whole mechanism: the
    /// server sends slot updates by inventory id, and the client looks that id up in the
    /// player's inventory manager. Contents are never invented here - the copy starts empty and
    /// is filled by the server when it is opened.
    /// </summary>
    public sealed class ModInventoryAccess
    {
        private readonly ICoreClientAPI _capi;
        private readonly ModInventoryKind _kind;
        private readonly string _key;
        private readonly int _size;
        private readonly string _id;

        private IInventory? _inventory;
        private bool _isOpen;

        private ModInventoryAccess(
            ICoreClientAPI capi,
            ModInventoryKind kind,
            string key,
            string id,
            int size,
            IInventory? inventory)
        {
            _capi = capi ?? throw new ArgumentNullException(nameof(capi));
            _kind = kind;
            _key = key;
            _id = id;
            _size = size;
            _inventory = inventory;

            ModInventoryChannel.Client(capi);
        }

        #region Building
        /// <summary>
        /// The inventory of a block entity of ours. The copy is the one the block entity on this
        /// client already holds - both sides bind it to the same <c>class-position</c> id when
        /// the block is placed, so there is nothing to mirror here.
        /// </summary>
        public static ModInventoryAccess ForBlock(ICoreClientAPI capi, BlockPos pos, IInventory inventory)
        {
            if (pos == null)
                throw new ArgumentNullException(nameof(pos));

            return new ModInventoryAccess(
                capi,
                ModInventoryKind.Block,
                ModInventorySystem.PosKey(pos),
                inventory?.InventoryID ?? "",
                0,
                inventory ?? throw new ArgumentNullException(nameof(inventory)));
        }

        /// <summary>
        /// One the server shares under this name. Every client that opens it sees the same
        /// contents, and a change one of them makes reaches the others.
        ///
        /// The size has to match the one the server registered - it is only used to build the
        /// copy on this side, and a copy with fewer slots would throw when an update for a slot
        /// it does not have arrives.
        /// </summary>
        public static ModInventoryAccess ForShared(ICoreClientAPI capi, string name, int size)
        {
            return new ModInventoryAccess(
                capi, ModInventoryKind.Shared, name, SharedInventoryId(name), size, null);
        }

        /// <summary>One the server keeps per player, under this class name.</summary>
        public static ModInventoryAccess ForPlayer(ICoreClientAPI capi, string className, int size)
        {
            return new ModInventoryAccess(
                capi, ModInventoryKind.Player, className, "", size, null);
        }

        internal static string SharedInventoryId(string name)
        {
            return "mvguishared-" + Sanitize(name);
        }

        internal static string PlayerInventoryId(string className, string playerUid)
        {
            return Sanitize(className) + "-" + playerUid;
        }

        /// <summary>
        /// InventoryBase splits an id at the first dash into a class name and an instance id, so
        /// a dash inside the name would move that split. Harmless for us, confusing for anything
        /// that reads the two apart - so they are kept out.
        /// </summary>
        private static string Sanitize(string name)
        {
            return name.Replace('-', '_');
        }
        #endregion

        /// <summary>
        /// The inventory to show. Null until the player exists, so read it when the dialog is
        /// built rather than at startup.
        /// </summary>
        public IInventory? Inventory => EnsureInventory();

        /// <summary>
        /// Where the packets a slot move produces go. Without this the move happens on this
        /// client alone and the server puts it back on the next sync.
        /// </summary>
        /// <summary>The client this access belongs to.</summary>
        public ICoreClientAPI Capi => _capi;

        public void SendPacket(object packet)
        {
            _capi.Network.SendPacketClient(packet);
        }

        /// <summary>Opens it on both sides. The grid calls this when its dialog is shown.</summary>
        public void Open()
        {
            IInventory? inventory = EnsureInventory();
            IPlayer? player = _capi.World?.Player;

            if (inventory == null || player == null || _isOpen)
                return;

            // The server first. Packets keep their order on the way out, so by the time the
            // first slot move arrives the inventory is registered there and the move is
            // accepted - the other way round it would be dropped as unknown.
            ModInventoryChannel.Client(_capi).SendPacket(
                new ModInventoryToggle { Kind = _kind, Key = _key, Opened = true });

            // Registers it with our own inventory manager and opens it here. Both are needed:
            // the manager is where incoming slot updates are looked up, and a shift click into
            // this grid only finds it if it is in there and open.
            player.InventoryManager.OpenInventory(inventory);
            _isOpen = true;
        }

        /// <summary>Closes it again on both sides, and asks the server to save it.</summary>
        public void Close()
        {
            IPlayer? player = _capi.World?.Player;

            if (_inventory == null || player == null || !_isOpen)
                return;

            player.InventoryManager.CloseInventory(_inventory);

            ModInventoryChannel.Client(_capi).SendPacket(
                new ModInventoryToggle { Kind = _kind, Key = _key, Opened = false });

            _isOpen = false;
        }

        private IInventory? EnsureInventory()
        {
            if (_inventory != null)
                return _inventory;

            string? playerUid = _capi.World?.Player?.PlayerUID;

            if (playerUid == null)
                return null;

            string id = _kind == ModInventoryKind.Player
                ? PlayerInventoryId(_key, playerUid)
                : _id;

            _inventory = new ModInventory(_size, id, _capi);
            return _inventory;
        }
    }
}
