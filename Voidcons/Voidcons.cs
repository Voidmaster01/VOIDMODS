// Voidcons.cs - source code of the Voidcons mod (v1.0.0) for FNAF Online Multiplayer.
//
// WHAT IT DOES
//   Lets every player show their OWN picture as their player icon on the character select screen, in the
//   same spot where the game shows backer icons. You put a PNG file in your own UserData\Voidcons folder,
//   and Voidcons shares it with the other Voidcons players in the lobby.
//   Icons belong to a player's CONNECTION (Netcode's client number for that lobby), never to a name, so
//   renaming yourself to someone else's name does not give you their icon. Player IDs are private: Voidcons
//   never sends them over the network, never writes them to the log, and only stores scrambled fingerprints.
//
// HOW TO BUILD AND RUN
//   Build: from the repo folder, pwsh -NoProfile -File build.ps1 -Mod Voidcons (after ModConfigs; build-all.ps1 does both).
//   Use:   everything is done in the game: home screen -> MOD CONFIGS -> Voidcons. There you choose a picture
//          (any PNG/JPG/BMP/GIF), move and crop it, and Voidcons compresses it to a 64 x 64 PNG. You can also
//          remove it, choose whether a backer icon is replaced (BackerOverride, off by default), forget
//          remembered backers, and reload.
//   Needs the ModConfigs mod (the MOD CONFIGS hub). MelonLoader says so if it's missing.
//
// THE FILES OF THIS MOD
//   Voidcons.cs (this file): networking, showing icons, BackerOverride, BackerGuard, Harmony patches.
//   ImageTools.cs: the compression system.  FilePicker.cs: the "Open file" window.
//   VoidconsPage.cs: the Voidcons page in MOD CONFIGS, with the move-and-crop editor.
//   (PNG reading and the VOIDMODS look come from ModConfigs: PngReader and UiKit.)
//
// WHAT IT NEEDS
//   MelonLoader 0.7 (it brings Harmony and Il2CppInterop). Icons only travel when the HOST of the lobby has
//   Voidcons too, and only players with Voidcons see them. Players without it see the normal game.
//
// HOW IT WORKS (the big picture)
//   The game already has a backer-icon system: LobbyUI.backerIcons maps a player NAME to a picture, and
//   CharacterSelectPlayer.HandleIcon shows that picture next to the player. Voidcons reuses that spot:
//     1. Your game reads UserData\Voidcons\icon.png when the mod loads.
//     2. When you are in a lobby, it sends "HELLO" and then your icon to the host, using Unity Netcode's
//        "named messages" (the game's own network connection, under the message name "Voidcons.v1").
//     3. Netcode tells the host which connection (client number) sent it, so nobody can send an icon for
//        someone else. The host passes "client number N has this icon" to every Voidcons player. A client
//        number only exists for that one lobby session, so it says nothing about who you are outside it.
//     4. Each Voidcons game shows the picture in the backer-icon spot after the game's own code ran.
//   Games without Voidcons never get these messages: the host only sends them to players who said HELLO.
//   Two extra parts live in their own classes at the bottom:
//     BackerOverride - a backer's own choice (off by default) to show their Voidcons picture instead.
//     BackerGuard    - hides a backer icon when someone uses a backer's name with a different player ID.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using ModConfigs;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Labels for MelonLoader: the main class, name, version, author, and the game this mod is for.
[assembly: MelonInfo(typeof(Voidcons.VoidconsMod), "Voidcons", "1.0.0", "Voidmaster01")]
[assembly: MelonGame("Brian_mpz", "FNAF Online Multiplayer")]
// Voidcons needs the ModConfigs mod: MelonLoader loads it first and warns the player if it's missing.
[assembly: MelonAdditionalDependencies("ModConfigs")]

namespace Voidcons
{
    // The main mod class. MelonLoader creates it and calls OnInitializeMelon once and OnUpdate every frame.
    // Most things are "static" (one shared copy) so the Harmony patches and helper classes can reach them too.
    public class VoidconsMod : MelonMod
    {
        // ---- Settings that never change ----
        // v2 = HD icons sent in pieces. A new name means older Voidcons builds simply don't hear these messages.
        private const string MessageName = "Voidcons.v2";      // the name our network messages travel under
        private const byte ProtocolVersion = 2;                // bumped if the message layout ever changes
        internal const int MaxPngBytes = 192 * 1024;           // biggest icon file accepted (192 KB; HD round icons are ~30-100 KB)
        internal const int MaxIconSize = 256;                  // biggest width/height in pixels (HD icons are 256 x 256)
        // The game's network layer only takes about 6 KB per message by default, so icons travel in 4000-byte
        // pieces and are put back together on the other side ("chunking").
        private const int ChunkBytes = 4000;
        private const int MaxChunks = (MaxPngBytes + ChunkBytes - 1) / ChunkBytes; // rounded up
        private const int MaxPacketBytes = ChunkBytes + 64;    // one piece + room for the small header

        // The first byte of every message says what kind of message it is. Piece headers:
        // [transfer number: 2 bytes][piece index: 2 bytes][piece count: 2 bytes] then that piece of the PNG.
        private const byte MsgHello = 1;   // client -> host: "I have Voidcons"
        private const byte MsgMyIcon = 2;  // client -> host: [flags][piece header][PNG piece] "here is (a piece of) my icon"
        private const byte MsgIcon = 3;    // host -> client: [flags][client number: 8 bytes][piece header][PNG piece]
        private const byte MsgRemove = 4;  // host -> client: [client number: 8 bytes] "this client left, drop their icon"

        // Bits in the "flags" byte. A flag is one yes/no switch stored in one bit of a number; "&" tests it.
        internal const byte FlagOverrideBacker = 1; // the sender chose to show this picture instead of their backer icon

        // ---- State ----
        internal static VoidconsMod Instance;                  // this mod, so static code can write to its log
        private static byte[] ownPng;                          // my own icon file, or null if there is none

        // One icon to show: the picture, and whether its owner wants it over their backer icon.
        internal sealed class IconEntry
        {
            public Sprite Sprite;
            public bool OverrideBacker;
        }
        // Icons to show, by client number (Netcode's number for each connection in this lobby).
        internal static readonly Dictionary<ulong, IconEntry> Icons = new Dictionary<ulong, IconEntry>();

        // What the HOST knows about each connected player, by their client number.
        private sealed class ClientState
        {
            public bool Modded;        // said HELLO, so it understands our messages
            public byte[] Png;         // the icon it uploaded, or null
            public byte Flags;         // the flags it sent with the icon
            public bool Shared;        // its icon has been shown and passed on
            public bool SeenInList;    // it has appeared in the game's player list at least once
            public float LastUpload = -100f; // when it last sent an icon (to ignore spam)
        }
        private static readonly Dictionary<ulong, ClientState> Clients = new Dictionary<ulong, ClientState>();

        private static bool sessionActive;   // our message handler is registered for the current connection
        private static bool helloSent;       // (client) HELLO and icon were sent to the host
        private static float nextPoll;       // time of the next slow check (twice per second is plenty)
        private static bool loggedMessageError;

        // ================= startup =================

        // The Voidcons folder, <game folder>\UserData\Voidcons, and my icon file inside it.
        internal static string Folder;
        private static string IconPath
        {
            get { return Path.Combine(Folder, "icon.png"); }
        }
        // The previous icon is kept here when a new one is chosen or the icon is removed, so nothing is lost.
        private static string BackupPath
        {
            get { return Path.Combine(Folder, "icon.previous.png"); }
        }

        // Runs once when MelonLoader loads the mod: make the folder, load the settings, read and check my own icon.
        public override void OnInitializeMelon()
        {
            Instance = this;
            // MelonEnvironment.UserDataDirectory is <game folder>\UserData. Path.Combine glues folder names together
            // with the right separators, so we never have to type backslashes ourselves.
            Folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Voidcons");
            Directory.CreateDirectory(Folder); // does nothing if the folder is already there
            WriteHowTo(Folder);
            BackerOverride.Load();
            BackerGuard.Load(Folder);
            LoggerInstance.Msg(LoadOwnIcon());
            // Our page in MOD CONFIGS. true = it's a VOIDMOD (logo in the list); "Voidcons" = our MelonPreferences
            // category, which this page already shows, so the hub doesn't add an automatic page for it too.
            ConfigHub.Register("Voidcons", "1.0.0", new Action<ConfigPage>(VoidconsPage.Build), true, "Voidcons");
        }

        // (Re)reads icon.png and checks it. Returns a sentence about the result, for the log and the panel.
        private static string LoadOwnIcon()
        {
            ownPng = null;
            if (!File.Exists(IconPath))
                return "No icon yet. Choose one in MOD CONFIGS -> Voidcons on the home screen.";
            byte[] bytes = File.ReadAllBytes(IconPath);
            // Check the file before using it, so a broken or huge picture is reported now, not in a lobby.
            string error = CheckPng(bytes, out int width, out int height);
            if (error != null)
                return $"icon.png can't be used: {error}. Choose a new picture on the home screen.";
            ownPng = bytes;
            return $"Your icon is loaded ({width}x{height}, {bytes.Length / 1024.0:0.0} KB).";
        }

        // Writes a short how-to text into the folder the first time, so players know what goes there.
        private static void WriteHowTo(string folder)
        {
            string file = Path.Combine(folder, "HOW TO.txt");
            if (File.Exists(file))
                return;
            File.WriteAllText(file,
                "Voidcons - your own player icon\r\n\r\n" +
                "Everything is done inside the game: home screen -> MOD CONFIGS -> Voidcons.\r\n" +
                "There you can choose a picture (PNG, JPG, BMP or GIF, any size), move and crop it\r\n" +
                "(Voidcons compresses it to 64 x 64), remove it, choose whether a backer icon is replaced,\r\n" +
                "forget remembered backers, and reload Voidcons.\r\n\r\n" +
                "Other players see your icon on the character select screen when they have Voidcons too,\r\n" +
                "and the lobby's host has Voidcons.\r\n\r\n" +
                "Files here: icon.png (your icon), icon.previous.png (the one before), backer_guard.dat\r\n" +
                "(BackerGuard's scrambled fingerprints; no names or player IDs).\r\n");
        }

        // ================= actions for the in-game panel =================
        // The Voidcons page (VoidconsPage.cs) calls these. Each returns a short sentence for the panel's status line.

        internal static byte[] OwnPng
        {
            get { return ownPng; }
        }

        // Saves a new icon (already compressed by ImageTools), keeping the old one as icon.previous.png.
        internal static string SaveOwnIcon(byte[] png)
        {
            string error = CheckPng(png, out _, out _);
            if (error != null)
                return "Not saved: " + error;
            if (File.Exists(IconPath))
                File.Copy(IconPath, BackupPath, true); // true = overwrite the older backup
            File.WriteAllBytes(IconPath, png);
            ownPng = png;
            return "Saved. Other Voidcons players see it from the next lobby you join or host.";
        }

        // Removes my icon. The file is moved to icon.previous.png instead of deleted, so it can come back.
        internal static string RemoveOwnIcon()
        {
            if (!File.Exists(IconPath))
                return "You have no icon to remove.";
            File.Copy(IconPath, BackupPath, true);
            File.Delete(IconPath);
            ownPng = null;
            return "Icon removed (a copy is kept as icon.previous.png).";
        }

        // Reloads everything Voidcons reads from disk: the settings file, BackerGuard's memory and my icon.
        // (A new Voidcons.dll itself can't be swapped in while the game runs; that still needs a restart.)
        internal static string Reload()
        {
            if (sessionActive)
                return "Leave the lobby first, then reload.";
            MelonPreferences.Load();      // re-reads UserData\MelonPreferences.cfg (the OverrideMyBackerIcon setting)
            BackerGuard.Load(Folder);
            string icon = LoadOwnIcon();
            Instance.LoggerInstance.Msg("Reloaded. " + icon);
            return "Reloaded. " + icon;
        }

        // Shared check for every icon, mine or received: size limit, then decode it fully with PngReader.
        // Returns null when the picture is fine, otherwise a reason in plain words.
        internal static string CheckPng(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length == 0)
                return "the file is empty";
            if (png.Length > MaxPngBytes)
                return $"the file is {png.Length / 1024} KB, the limit is {MaxPngBytes / 1024} KB";
            byte[] pixels = PngReader.Decode(png, MaxIconSize, out width, out height, out string error);
            return pixels == null ? error : null;
        }

        // ================= every frame =================

        // OnUpdate runs every frame. It notices when a network session starts or ends and does the slow
        // checks (noticing players who joined or left) twice per second.
        public override void OnUpdate()
        {
            VoidconsPage.Tick(); // the Voidcons page collects finished picture picks here (main thread)
            NetworkManager nm = NetworkManager.Singleton;
            bool live = nm != null && nm.IsListening && !nm.ShutdownInProgress;
            if (!live)
            {
                if (sessionActive)
                    EndSession();
                return;
            }
            if (!sessionActive)
                StartSession(nm);
            if (Time.unscaledTime < nextPoll)
                return;
            nextPoll = Time.unscaledTime + 0.5f;
            try
            {
                if (nm.IsServer)
                    HostPoll();
                else
                    ClientPoll();
            }
            catch (Exception e)
            {
                LogOnce($"Voidcons check failed: {e.Message}");
            }
        }

        // A new connection (hosting or joining): listen for our messages. On the host, my own icon is
        // stored like any other player's, under the host's own client id.
        private void StartSession(NetworkManager nm)
        {
            // "new Action<ulong, FastBufferReader>(OnMessage)" wraps our method in a C# delegate (a value that
            // points to a method). The game's HandleNamedMessageDelegate type has an "implicit operator" that
            // turns such a delegate into one the game's Netcode can call, so we can pass it in directly.
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, new Action<ulong, FastBufferReader>(OnMessage));
            sessionActive = true;
            helloSent = false;
            Clients.Clear();
            if (nm.IsServer && ownPng != null)
            {
                ClientState me = GetClient(NetworkManager.ServerClientId);
                me.Png = ownPng;
                me.Flags = BackerOverride.Flags();
            }
            LoggerInstance.Msg(nm.IsServer ? "Hosting: Voidcons shares icons between Voidcons players." : "Joined: telling the host about Voidcons.");
        }

        // The connection ended: forget everything about it and free the pictures.
        private static void EndSession()
        {
            sessionActive = false;
            try
            {
                NetworkManager nm = NetworkManager.Singleton;
                if (nm != null && nm.CustomMessagingManager != null)
                    nm.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
            }
            catch (Exception) { } // the network side may already be gone; nothing to clean up then
            Clients.Clear();
            InProgress.Clear(); // unfinished piece collections belong to the old connection
            foreach (IconEntry entry in Icons.Values)
                DestroySprite(entry.Sprite);
            Icons.Clear();
        }

        // ================= client side =================

        // As a joining player: once the game's player list has arrived (we're really in), say HELLO and send
        // my icon to the host, once. If the host has no Voidcons, it simply ignores both.
        private static void ClientPoll()
        {
            if (helloSent)
                return;
            MultiplayerManager mm = MultiplayerManager.Instance;
            if (mm == null || mm.playerDataList == null || mm.playerDataList.Count == 0)
                return;
            Send(NetworkManager.ServerClientId, new byte[] { MsgHello, ProtocolVersion });
            if (ownPng != null)
                SendPieces(NetworkManager.ServerClientId, new byte[] { MsgMyIcon, BackerOverride.Flags() }, ownPng);
            helloSent = true;
        }

        // ================= host side =================

        // As the host, twice per second: share icons of players who are now really in the game's player list,
        // and notice who left. (A player can send their icon a moment before the game has added them.)
        private static void HostPoll()
        {
            MultiplayerManager mm = MultiplayerManager.Instance;
            if (mm == null || mm.playerDataList == null)
                return;

            // Read the game's player list once: client number -> player name.
            var names = new Dictionary<ulong, string>();
            for (int i = 0; i < mm.playerDataList.Count; i++)
            {
                PlayerData pd = mm.playerDataList[i];
                names[pd.clientId] = pd.playerName.ToString();
            }

            // A copy of the keys, because we may remove entries while going through them
            // (changing a Dictionary while a foreach walks it is not allowed in C#).
            foreach (ulong clientId in new List<ulong>(Clients.Keys))
            {
                ClientState c = Clients[clientId];
                bool inList = names.TryGetValue(clientId, out string name) && !string.IsNullOrEmpty(name);
                if (inList)
                    c.SeenInList = true;
                else if (c.SeenInList)
                {
                    // This player was in the list and isn't any more: they left.
                    if (c.Shared)
                    {
                        RemoveIcon(clientId);
                        Broadcast(Join(new byte[] { MsgRemove }, BitConverter.GetBytes(clientId)));
                    }
                    Clients.Remove(clientId);
                    continue;
                }

                // This sender is now in the player list: show their icon here and pass it on.
                if (inList && c.Png != null && !c.Shared)
                {
                    if (IsBlockedName(name))
                    {
                        c.Png = null;
                        continue;
                    }
                    ShareIcon(clientId, c);
                }
            }
        }

        // Show one player's icon on the host and send it to every Voidcons player.
        private static void ShareIcon(ulong clientId, ClientState c)
        {
            c.Shared = true;
            ApplyIcon(clientId, c.Png, c.Flags);
            foreach (KeyValuePair<ulong, ClientState> pair in Clients)
                if (pair.Value.Modded && pair.Key != NetworkManager.ServerClientId)
                    SendIcon(pair.Key, clientId, c.Png, c.Flags);
        }

        // Sends a message to every player that said HELLO (never to the host itself, which has it already).
        private static void Broadcast(byte[] message)
        {
            foreach (KeyValuePair<ulong, ClientState> pair in Clients)
                if (pair.Value.Modded && pair.Key != NetworkManager.ServerClientId)
                    Send(pair.Key, message);
        }

        // Finds or creates the host's record for one client id. "out" hands back the found value.
        private static ClientState GetClient(ulong id)
        {
            if (!Clients.TryGetValue(id, out ClientState c))
            {
                c = new ClientState();
                Clients[id] = c;
            }
            return c;
        }

        // ================= messages =================

        // Netcode calls this when a "Voidcons.v1" message arrives. sender = the network id of who sent it.
        // try/catch: a broken message from someone must never crash the game, so any error is only logged.
        private static void OnMessage(ulong sender, FastBufferReader reader)
        {
            try
            {
                byte[] data = ReadAll(reader);
                if (data.Length == 0)
                    return;
                NetworkManager nm = NetworkManager.Singleton;
                if (nm == null)
                    return;

                // "switch" picks the case that matches the first byte (the message kind).
                switch (data[0])
                {
                    case MsgHello:
                        if (!nm.IsServer)
                            return;
                        GetClient(sender).Modded = true;
                        // Catch the newcomer up: send every icon that is already known.
                        foreach (KeyValuePair<ulong, ClientState> pair in Clients)
                            if (pair.Value.Shared && pair.Value.Png != null)
                                SendIcon(sender, pair.Key, pair.Value.Png, pair.Value.Flags);
                        break;

                    case MsgMyIcon:
                    {
                        // [kind][flags][piece header][piece]: collect pieces until the whole PNG is here.
                        if (!nm.IsServer || data.Length < 9)
                            return;
                        ClientState c = GetClient(sender);
                        c.Modded = true;
                        byte[] png = Assemble(sender, data, 2);
                        if (png == null)
                            return; // not complete yet (or a broken piece)
                        if (Time.unscaledTime - c.LastUpload < 5f)
                            return; // more than one upload in 5 seconds: ignore (protects against spam)
                        c.LastUpload = Time.unscaledTime;
                        string error = CheckPng(png, out _, out _);
                        if (error != null)
                        {
                            Instance.LoggerInstance.Warning($"Ignored an icon from client {sender}: {error}");
                            return;
                        }
                        c.Png = png;
                        c.Flags = data[1];
                        if (c.Shared)
                            ShareIcon(sender, c); // already in the game (a new picture): share it right away
                        break;                    // otherwise HostPoll shares it once the game has added them
                    }

                    case MsgIcon:
                    {
                        // Only the host may tell us about icons. NetworkManager.ServerClientId is the host's number.
                        // Layout: [kind][flags][client number: 8 bytes][piece header][piece].
                        // BitConverter turns 8 bytes into a number. Pieces are collected per client number.
                        if (nm.IsServer || sender != NetworkManager.ServerClientId || data.Length < 17)
                            return;
                        ulong owner = BitConverter.ToUInt64(data, 2);
                        byte[] png = Assemble(owner, data, 10);
                        if (png != null)
                            ApplyIcon(owner, png, data[1]);
                        break;
                    }

                    case MsgRemove:
                    {
                        if (nm.IsServer || sender != NetworkManager.ServerClientId || data.Length < 9)
                            return;
                        RemoveIcon(BitConverter.ToUInt64(data, 1));
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                LogOnce($"Voidcons message failed: {e.Message}");
            }
        }

        // ---- sending and receiving icons in pieces ----

        private static ushort transferCounter; // numbers each icon transfer, so pieces of different icons never mix

        // Sends a PNG as pieces. "header" is what goes in front of every piece (kind, flags, maybe a client number),
        // then [transfer][index][count] and up to ChunkBytes of the PNG.
        private static void SendPieces(ulong target, byte[] header, byte[] png)
        {
            ushort transfer = ++transferCounter;
            int count = (png.Length + ChunkBytes - 1) / ChunkBytes; // pieces needed, rounded up
            for (int i = 0; i < count; i++)
            {
                int start = i * ChunkBytes;
                int size = Math.Min(ChunkBytes, png.Length - start);
                var piece = new byte[header.Length + 6 + size];
                Buffer.BlockCopy(header, 0, piece, 0, header.Length);
                int p = header.Length;
                BitConverter.GetBytes(transfer).CopyTo(piece, p);       // 2 bytes each (ushort = 0..65535)
                BitConverter.GetBytes((ushort)i).CopyTo(piece, p + 2);
                BitConverter.GetBytes((ushort)count).CopyTo(piece, p + 4);
                Buffer.BlockCopy(png, start, piece, p + 6, size);
                Send(target, piece);
            }
        }

        // ICON pieces for one receiver: [kind][flags][client number: 8 bytes] in front of every piece.
        private static void SendIcon(ulong target, ulong owner, byte[] png, byte flags)
        {
            SendPieces(target, Join(new byte[] { MsgIcon, flags }, BitConverter.GetBytes(owner)), png);
        }

        // Pieces being collected, per owner (the sender's client number on the host, the icon owner on a client).
        private sealed class Incoming
        {
            public ushort Transfer;
            public byte[][] Parts;  // an array of byte arrays: one slot per piece
            public int Received;
        }
        private static readonly Dictionary<ulong, Incoming> InProgress = new Dictionary<ulong, Incoming>();

        // Adds one piece (its piece header starts at "at"). Returns the whole PNG once every piece has arrived,
        // otherwise null. A piece from a NEW transfer throws away an unfinished older one.
        private static byte[] Assemble(ulong owner, byte[] data, int at)
        {
            ushort transfer = BitConverter.ToUInt16(data, at);
            int index = BitConverter.ToUInt16(data, at + 2);
            int count = BitConverter.ToUInt16(data, at + 4);
            if (count == 0 || count > MaxChunks || index >= count)
                return null;
            if (!InProgress.TryGetValue(owner, out Incoming inc) || inc.Transfer != transfer || inc.Parts.Length != count)
            {
                inc = new Incoming { Transfer = transfer, Parts = new byte[count][] };
                InProgress[owner] = inc;
            }
            if (inc.Parts[index] == null)
            {
                inc.Parts[index] = Slice(data, at + 6);
                inc.Received++;
            }
            if (inc.Received < count)
                return null;
            InProgress.Remove(owner);
            // All pieces are here: glue them together in order.
            int total = 0;
            foreach (byte[] part in inc.Parts)
                total += part.Length;
            if (total > MaxPngBytes)
                return null;
            var png = new byte[total];
            int pos = 0;
            foreach (byte[] part in inc.Parts)
            {
                Buffer.BlockCopy(part, 0, png, pos, part.Length);
                pos += part.Length;
            }
            return png;
        }

        // Sends one message to one player. A FastBufferWriter is Netcode's buffer for outgoing data: we create it
        // as big as the message, copy our bytes in, send it, and Dispose it (free its memory) in "finally", which
        // runs even if sending fails. ReliableFragmentedSequenced = arrives for sure and in order.
        private static void Send(ulong target, byte[] message)
        {
            var writer = new FastBufferWriter(message.Length + 16, Allocator.Temp, message.Length + 16);
            try
            {
                // byte[] turns into the game's array type (Il2CppStructArray<byte>) by itself here.
                writer.WriteBytesSafe(message, message.Length, 0);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MessageName, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally
            {
                writer.Dispose();
            }
        }

        // Copies a received message out of Netcode's reader into a normal C# byte array.
        private static byte[] ReadAll(FastBufferReader reader)
        {
            int length = reader.Length - reader.Position;
            if (length <= 0 || length > MaxPacketBytes)
                return Array.Empty<byte>();
            var buffer = new Il2CppStructArray<byte>(length);
            reader.ReadBytesSafe(ref buffer, length, 0);
            var result = new byte[length];
            for (int i = 0; i < length; i++)
                result[i] = buffer[i];
            return result;
        }

        // Small byte helpers: Join glues two arrays together, Slice copies everything from "start" on.
        private static byte[] Join(byte[] a, byte[] b)
        {
            var result = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            return result;
        }

        private static byte[] Slice(byte[] data, int start)
        {
            var result = new byte[data.Length - start];
            Buffer.BlockCopy(data, start, result, 0, result.Length);
            return result;
        }

        // ================= pictures on screen =================

        // The developer's name never gets a Voidcons icon (the game guards it too), and an empty name can't match anyone.
        internal static bool IsBlockedName(string name)
        {
            return string.IsNullOrEmpty(name) || string.Equals(name, "brian_mpz", StringComparison.OrdinalIgnoreCase);
        }

        // Turns PNG bytes into a Unity Sprite, stores it for that client number, and refreshes the visible slots.
        private static void ApplyIcon(ulong clientId, byte[] png, byte flags)
        {
            Sprite sprite = MakeSprite(png);
            if (sprite == null)
                return;
            if (Icons.TryGetValue(clientId, out IconEntry old))
                DestroySprite(old.Sprite);
            Icons[clientId] = new IconEntry { Sprite = sprite, OverrideBacker = (flags & FlagOverrideBacker) != 0 };
            RefreshSlots();
        }

        // Drops a player's icon. Their slot is refreshed by the game itself when the player list changes.
        private static void RemoveIcon(ulong clientId)
        {
            if (Icons.TryGetValue(clientId, out IconEntry old))
            {
                Icons.Remove(clientId);
                DestroySprite(old.Sprite);
            }
        }

        // Builds a Sprite (Unity's picture for UI) from PNG bytes, with ModConfigs' shared helper: PngReader turns
        // the file into raw pixels (Unity's own PNG reader was left out of this game), then the pixels go into a
        // texture and a sprite (see UiKit.SpriteFromRgba in ModConfigs for the details).
        internal static Sprite MakeSprite(byte[] png)
        {
            Sprite sprite = UiKit.SpriteFromPng(png, MaxIconSize, out string error);
            if (sprite == null)
                Instance.LoggerInstance.Warning($"Icon could not be read: {error}");
            return sprite;
        }

        // Frees a sprite and its texture (ModConfigs' shared helper).
        internal static void DestroySprite(Sprite sprite)
        {
            UiKit.DestroySprite(sprite);
        }

        // Goes over every character select slot that exists right now and applies our rules to it.
        // FindObjectsOfType with Il2CppType.Of<T>() is the IL2CPP way to ask Unity "give me all objects of this type";
        // TryCast turns each result back into a CharacterSelectPlayer.
        private static void RefreshSlots()
        {
            foreach (UnityEngine.Object found in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<CharacterSelectPlayer>()))
            {
                CharacterSelectPlayer slot = found.TryCast<CharacterSelectPlayer>();
                if (slot != null)
                    ApplyToSlot(slot);
            }
        }

        // Decides what one player slot shows, AFTER the game's own HandleIcon has run:
        //   1. Who is in this slot? The game's own GetPlayerDataFromPlayerIndex gives the name, the client number
        //      and the player ID (which the game already keeps for every player; it stays on this PC).
        //   2. BackerGuard: is this a backer name used by a different player than first seen? Then that
        //      backer icon is not theirs: hide it (they may still show their own Voidcons picture below).
        //   3. A real backer keeps their backer icon unless THEY turned on BackerOverride.
        //   4. Otherwise, if this client number has a Voidcons picture, show it in the icon spot.
        internal static void ApplyToSlot(CharacterSelectPlayer slot)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy)
                return;
            PlayerData pd;
            try { pd = slot.GetPlayerDataFromPlayerIndex(); }
            catch (Exception) { return; } // an empty slot has no player
            string name = pd.playerName.ToString();
            if (IsBlockedName(name))
                return;

            bool backerName = BackerGuard.IsBackerName(name);
            bool impostor = backerName && !BackerGuard.IsRightPlayer(name, pd.authenticationID.ToString());
            Icons.TryGetValue(pd.clientId, out IconEntry entry);

            if (backerName && !impostor && (entry == null || !entry.OverrideBacker))
                return; // the real backer, keeping their own backer icon (the game already shows it)

            if (entry != null && entry.Sprite != null)
            {
                slot.icon.SetActive(true);
                slot.iconInnerObject.sprite = entry.Sprite;
            }
            else if (impostor)
                slot.icon.SetActive(false); // a borrowed backer name: no backer icon, like the game's developer check
        }

        // Writes an error to the log only the first time, so a repeating problem can't flood the log.
        private static void LogOnce(string message)
        {
            if (loggedMessageError)
                return;
            loggedMessageError = true;
            Instance.LoggerInstance.Warning(message + " (further errors of this kind are not logged)");
        }
    }

    // ================= BackerOverride =================
    // A backer's own choice to show their Voidcons picture instead of their backer icon. Off by default.
    // It is a MelonPreferences setting: MelonLoader saves it in UserData\MelonPreferences.cfg under [Voidcons],
    // where the player can change it with any text editor. Only your own game reads your setting, and it is sent
    // along with your own icon, which the host ties to your own player ID, so nobody can switch it on for you.
    internal static class BackerOverride
    {
        private static MelonPreferences_Entry<bool> entry;

        public static void Load()
        {
            MelonPreferences_Category category = MelonPreferences.CreateCategory("Voidcons");
            entry = category.CreateEntry("OverrideMyBackerIcon", false,
                description: "Backers only: true = show your Voidcons icon.png instead of your backer icon. Default false.");
        }

        public static bool IsOn
        {
            get { return entry != null && entry.Value; }
        }

        // The flags byte for my own icon message.
        public static byte Flags()
        {
            return IsOn ? VoidconsMod.FlagOverrideBacker : (byte)0;
        }

        // Changes the setting from the in-game panel and saves it to MelonPreferences.cfg right away.
        public static void Set(bool on)
        {
            if (entry == null)
                return;
            entry.Value = on;
            MelonPreferences.Save();
        }
    }

    // ================= BackerGuard =================
    // The game shows backer icons by NAME only, so anyone who renames themselves to a backer's name gets that icon
    // (only "brian_mpz" is protected, by the game's developer check). BackerGuard adds the same kind of check for
    // backers on Voidcons games ("remember first seen"): the first time it sees a backer name, it remembers WHO
    // used it; later, the same name used by a different player is treated as borrowed and the backer icon is
    // hidden for that player. The game's own code is not changed.
    //
    // PRIVACY: player IDs are private, so the file never holds a name or an ID. It holds "fingerprints" made
    // with HMAC-SHA256: a one-way scramble with a secret key. The same input always gives the same fingerprint,
    // so a new sighting can be compared with the stored one, but a fingerprint can't be turned back into the
    // name or ID, not even by the file's owner. The key is 32 random bytes made on this PC the first time, so
    // fingerprints differ from PC to PC and files can't be compared with each other either.
    // File UserData\Voidcons\backer_guard.dat: line 1 = the key (Base64 text), then "name-fingerprint id-fingerprint" lines.
    internal static class BackerGuard
    {
        private static string file;
        private static byte[] key;
        // name fingerprint -> player ID fingerprint first seen with it.
        private static readonly Dictionary<string, string> FirstSeen = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reads the key and the stored fingerprints, or makes a new random key the first time.
        public static void Load(string folder)
        {
            file = Path.Combine(folder, "backer_guard.dat");
            key = null;          // start clean, so a reload really re-reads the file
            FirstSeen.Clear();
            Warned.Clear();
            try
            {
                if (File.Exists(file))
                {
                    string[] lines = File.ReadAllLines(file);
                    key = Convert.FromBase64String(lines[0]); // Base64 = a way to write bytes as plain letters
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string[] parts = lines[i].Split(' ');
                        if (parts.Length == 2)
                            FirstSeen[parts[0]] = parts[1];
                    }
                }
            }
            catch (Exception)
            {
                key = null; // a damaged file: start again with a new key below
                FirstSeen.Clear();
            }
            if (key == null || key.Length != 32)
            {
                // RandomNumberGenerator gives unpredictable bytes, made for secrets (unlike the plain Random class).
                key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                FirstSeen.Clear();
                Save();
            }
        }

        // How many backers are remembered (for the panel).
        public static int Count
        {
            get { return FirstSeen.Count; }
        }

        // "Forget remembered backers" in the panel: delete the file and start over with a brand-new key.
        public static void Reset()
        {
            if (File.Exists(file))
                File.Delete(file);
            Load(Path.GetDirectoryName(file));
        }

        // The one-way fingerprint of a text. Names are lower-cased first, because the game's backer list ignores
        // upper/lower case ("Void" and "void" are the same backer). The result is written as hex text.
        private static string Fingerprint(string text)
        {
            using (var hmac = new System.Security.Cryptography.HMACSHA256(key))
                return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        // True when the game's own backer list (LobbyUI.backerIcons) has a picture for this name.
        public static bool IsBackerName(string name)
        {
            var backers = LobbyUI.backerIcons;
            return backers != null && backers.ContainsKey(name);
        }

        // True when this player is the one first seen with this backer name. The first sighting is saved.
        // An empty player ID can't be checked, so it is let through rather than hiding a real backer by mistake.
        public static bool IsRightPlayer(string name, string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || key == null)
                return true;
            string nameFp = Fingerprint("name:" + name.ToLowerInvariant());
            string idFp = Fingerprint("id:" + playerId);
            if (FirstSeen.TryGetValue(nameFp, out string known))
            {
                if (known == idFp)
                    return true;
                // The backer NAME is public (it's on screen), so it may be logged; the player ID never is.
                if (Warned.Add(name)) // HashSet.Add is true only the first time, so this warns once per name
                    VoidconsMod.Instance.LoggerInstance.Warning($"BackerGuard: '{name}' is a backer name, but used by a different player than first seen. Backer icon hidden.");
                return false;
            }
            FirstSeen[nameFp] = idFp;
            Save();
            return true;
        }

        // Writes the key and all fingerprints back to the file. To forget every backer, delete the file.
        private static void Save()
        {
            try
            {
                var lines = new List<string> { Convert.ToBase64String(key) };
                foreach (KeyValuePair<string, string> pair in FirstSeen)
                    lines.Add(pair.Key + " " + pair.Value);
                File.WriteAllLines(file, lines);
            }
            catch (Exception e)
            {
                VoidconsMod.Instance.LoggerInstance.Warning($"BackerGuard could not save {file}: {e.Message}");
            }
        }
    }

    // ================= Harmony patches =================
    // A Postfix runs right AFTER the game's own method; __instance is the slot the method ran on. The game first
    // shows (or hides) the real backer icon, then ApplyToSlot applies Voidcons' rules to that slot.
    // Both methods are patched: IL2CPP may have merged the small HandleIcon into UpdatePlayer ("inlining"),
    // in which case only the UpdatePlayer patch fires. Running both is harmless.

    [HarmonyPatch(typeof(CharacterSelectPlayer), nameof(CharacterSelectPlayer.HandleIcon))]
    internal static class HandleIconPatch
    {
        private static void Postfix(CharacterSelectPlayer __instance)
        {
            try { VoidconsMod.ApplyToSlot(__instance); }
            catch (Exception) { } // never break the game's own screen because of an icon
        }
    }

    [HarmonyPatch(typeof(CharacterSelectPlayer), nameof(CharacterSelectPlayer.UpdatePlayer))]
    internal static class UpdatePlayerPatch
    {
        private static void Postfix(CharacterSelectPlayer __instance)
        {
            try { VoidconsMod.ApplyToSlot(__instance); }
            catch (Exception) { }
        }
    }
}
