using ProtoBuf;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ModernVintageGUI.Inventory
{
    /// <summary>
    /// Client to server: the dialog showing one of these inventories was opened or closed.
    /// </summary>
    [ProtoContract]
    public class DialogInventoryToggle
    {
        [ProtoMember(1)]
        public string ClassName = "";

        [ProtoMember(2)]
        public bool Opened;
    }

    /// <summary>
    /// The channel both halves talk over. Registered on first use, because either half may come
    /// up first and a channel may only be registered once per side.
    /// </summary>
    internal static class DialogInventoryNetwork
    {
        public const string ChannelName = "modernvintagegui";

        public static IClientNetworkChannel Client(ICoreClientAPI capi)
        {
            return capi.Network.GetChannel(ChannelName)
                ?? capi.Network.RegisterChannel(ChannelName)
                       .RegisterMessageType<DialogInventoryToggle>();
        }

        public static IServerNetworkChannel Server(ICoreServerAPI sapi)
        {
            return sapi.Network.GetChannel(ChannelName)
                ?? sapi.Network.RegisterChannel(ChannelName)
                       .RegisterMessageType<DialogInventoryToggle>();
        }
    }

    /// <summary>
    /// The server half of a mod owned inventory: one real inventory per player, saved with that
    /// player, and registered with them while the dialog is open.
    ///
    /// The registration is the entire point, and it is not optional decoration. Every slot move
    /// the client sends names its inventory by id, and ServerSystemInventory resolves that id
    /// through <c>player.InventoryManager.GetInventory(id)</c> - an inventory that is not in
    /// there does not exist as far as the server is concerned, so every move for it is dropped
    /// on the floor and the client is corrected back on the next sync. That is the difference
    /// between an inventory and a picture of one. <c>OpenInventory</c> does both halves: it puts
    /// the inventory into the player's manager and opens it, and from that moment the server
    /// syncs its dirty slots to that client on its own, exactly as it does for the hotbar.
    ///
    /// Registering it also gets the rest for free, because everything else works off the same
    /// list: shift click moves stacks into it (TryTransferAway walks the opened inventories),
    /// and taking an item out of the creative inventory into it works, because
    /// HandleCreateItemstack resolves its target the same way.
    /// </summary>
    public sealed class DialogInventoryServer
    {
        private sealed class Entry
        {
            public InventoryGeneric Inventory = null!;
            public string PlayerUid = "";
            public string ClassName = "";
        }

        private readonly ICoreServerAPI _sapi;
        private readonly Dictionary<string, int> _slotCountByClass = new Dictionary<string, int>();
        private readonly Dictionary<string, Entry> _byInventoryId = new Dictionary<string, Entry>();

        public DialogInventoryServer(ICoreServerAPI sapi)
        {
            _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));

            DialogInventoryNetwork.Server(sapi).SetMessageHandler<DialogInventoryToggle>(OnToggle);

            sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
            sapi.Event.GameWorldSave += SaveAll;
        }

        /// <summary>
        /// Declares an inventory this mod offers. The client may only open a class name that was
        /// declared here - the slot count has to be decided by the server, or a client could ask
        /// for an inventory of any size it liked.
        /// </summary>
        public void Register(string className, int slotCount)
        {
            if (string.IsNullOrEmpty(className))
                throw new ArgumentException("A dialog inventory needs a class name", nameof(className));

            // The id is className-playerUID and InventoryBase splits it at the first dash, so a
            // dash in the class name would quietly move the split and produce a different id on
            // the two sides.
            if (className.Contains('-'))
                throw new ArgumentException("A dialog inventory class name must not contain '-'", nameof(className));

            _slotCountByClass[className] = Math.Max(1, slotCount);
        }

        private void OnToggle(IServerPlayer player, DialogInventoryToggle message)
        {
            if (message?.ClassName == null)
                return;

            if (!_slotCountByClass.TryGetValue(message.ClassName, out int slotCount))
            {
                _sapi.Logger.Notification(
                    "[ModernVintageGUI] {0} asked to open the unknown dialog inventory '{1}'.",
                    player.PlayerName, message.ClassName);
                return;
            }

            Entry entry = GetOrLoad(player, message.ClassName, slotCount);

            if (message.Opened)
            {
                player.InventoryManager.OpenInventory(entry.Inventory);

                // Nothing has changed since it was loaded, so the dirty slot sync would send
                // nothing and the client would show an empty grid. Push every slot once - empty
                // ones included, so a slot the client still remembers from last time is cleared.
                for (int i = 0; i < entry.Inventory.Count; i++)
                {
                    entry.Inventory.MarkSlotDirty(i);
                }
            }
            else
            {
                player.InventoryManager.CloseInventory(entry.Inventory);
                Save(player, entry);
            }
        }

        private Entry GetOrLoad(IServerPlayer player, string className, int slotCount)
        {
            string inventoryId = className + "-" + player.PlayerUID;

            if (_byInventoryId.TryGetValue(inventoryId, out Entry? existing))
                return existing;

            var inventory = new InventoryGeneric(slotCount, inventoryId, _sapi);

            byte[] stored = player.GetModdata(ModdataKey(className));

            if (stored != null && stored.Length > 0)
            {
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
                        "[ModernVintageGUI] Could not read the stored '{0}' inventory of {1}, " +
                        "starting empty: {2}", className, player.PlayerName, e);
                }
            }

            var entry = new Entry
            {
                Inventory = inventory,
                PlayerUid = player.PlayerUID,
                ClassName = className
            };

            _byInventoryId[inventoryId] = entry;
            return entry;
        }

        private void Save(IServerPlayer player, Entry entry)
        {
            var tree = new TreeAttribute();
            entry.Inventory.ToTreeAttributes(tree);
            player.SetModdata(ModdataKey(entry.ClassName), tree.ToBytes());
        }

        private void SaveAll()
        {
            foreach (Entry entry in _byInventoryId.Values)
            {
                if (_sapi.World.PlayerByUid(entry.PlayerUid) is IServerPlayer player)
                {
                    Save(player, entry);
                }
            }
        }

        private void OnPlayerDisconnect(IServerPlayer player)
        {
            var goneIds = new List<string>();

            foreach (KeyValuePair<string, Entry> pair in _byInventoryId)
            {
                if (pair.Value.PlayerUid != player.PlayerUID)
                    continue;

                Save(player, pair.Value);
                goneIds.Add(pair.Key);
            }

            // Dropped rather than kept: the contents are on the player now, and holding the
            // object would leak one inventory per player per session.
            foreach (string id in goneIds)
            {
                _byInventoryId.Remove(id);
            }
        }

        private static string ModdataKey(string className)
        {
            return "modernvintagegui-" + className;
        }
    }

    /// <summary>
    /// The client half: the copy of the inventory this client works with, and the two messages
    /// that tell the server when the dialog holding it opens and closes.
    ///
    /// Both sides create an inventory with the same id and the same slot count, which is what
    /// lets the server address ours: it sends slot updates by inventory id, and the client looks
    /// that id up in the player's inventory manager. Contents are never invented here - the copy
    /// starts empty and is filled by the server on open.
    /// </summary>
    public sealed class DialogInventory
    {
        private readonly ICoreClientAPI _capi;
        private readonly string _className;
        private readonly int _slotCount;

        private InventoryGeneric? _inventory;
        private bool _isOpen;

        public DialogInventory(ICoreClientAPI capi, string className, int slotCount)
        {
            _capi = capi ?? throw new ArgumentNullException(nameof(capi));
            _className = className ?? throw new ArgumentNullException(nameof(className));
            _slotCount = Math.Max(1, slotCount);

            DialogInventoryNetwork.Client(capi);
        }

        /// <summary>
        /// The inventory to hand to <see cref="ControlTypes.InventoryGridControl.SetInventory"/>.
        /// Null until the player exists, so read it when the dialog is built rather than at
        /// startup.
        /// </summary>
        public IInventory? Inventory => EnsureInventory();

        /// <summary>
        /// Where the grid's slot move packets go. Pass this as the sendPacket argument of
        /// SetInventory - without it the move happens on this client alone and the server puts
        /// it back on the next sync.
        /// </summary>
        public void SendPacket(object packet)
        {
            _capi.Network.SendPacketClient(packet);
        }

        /// <summary>
        /// Opens the inventory on both sides. Call it when the dialog is shown, and note that
        /// the grid must be told <c>announceOpen: false</c>, because this does that job for it
        /// and does it on the server as well.
        /// </summary>
        public void Open()
        {
            InventoryGeneric? inventory = EnsureInventory();
            IPlayer? player = _capi.World?.Player;

            if (inventory == null || player == null || _isOpen)
                return;

            // The server first. Packets keep their order on the way out, so by the time the
            // first slot move arrives the inventory is registered there and the move is
            // accepted - the other way round it would be dropped as unknown.
            DialogInventoryNetwork.Client(_capi).SendPacket(
                new DialogInventoryToggle { ClassName = _className, Opened = true });

            // Registers it with our own inventory manager and opens it here. Both are needed:
            // the manager is where incoming slot updates are looked up, and a shift click into
            // this grid only finds it if it is in there and open.
            //
            // The packet OpenInventory hands back is the vanilla open notification, which the
            // request above already covers - and which the server would drop anyway, since it
            // arrives before the inventory is known there.
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

            DialogInventoryNetwork.Client(_capi).SendPacket(
                new DialogInventoryToggle { ClassName = _className, Opened = false });

            _isOpen = false;
        }

        private InventoryGeneric? EnsureInventory()
        {
            if (_inventory != null)
                return _inventory;

            string? playerUid = _capi.World?.Player?.PlayerUID;
            if (playerUid == null)
                return null;

            _inventory = new InventoryGeneric(_slotCount, _className + "-" + playerUid, _capi);
            return _inventory;
        }
    }
}
