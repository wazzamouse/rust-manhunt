/*
* Copyright (c) 2023 James Taylor (jim@jrtaylor.com)
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/

using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using UnityEngine;
using System.Linq;
using System.Globalization;
using System.IO;
using Rust;
using Oxide.Game.Rust.Libraries;
using Facepunch;
using UnityEngine;
using System.Runtime.InteropServices;

namespace Oxide.Plugins
{
    [Info("ManHunt", "WazzaMouse", "1.0.0")]
    [Description("Timed event where players hunt the selected player for fun and rewards.")]
    class ManHunt : RustPlugin {
        
        [PluginReference] Plugin ServerRewards, Clans;

        private Configuration config;
        public static StoredData data { get; set; } = new StoredData();
        public Dictionary<BasePlayer, string> huntedPlayers = new Dictionary<BasePlayer, string>();
        public Dictionary<BasePlayer, string> hunters = new Dictionary<BasePlayer, string>();
        public Dictionary<string, string> tmpAdmin { get; set; } = new Dictionary<string, string>();

        public Vector3 outpostPos;
        private bool eventInProgress = false;

        #region Oxide Hooks
        private void Loaded()
        {
            permission.RegisterPermission("manhunt.admin", this);
        }
        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
            }
        }
        private void DestroyUI(BasePlayer player)
        {
            if (playerTimerCUIs.ContainsKey(player))
            {
                foreach (var containerName in playerTimerCUIs[player])
                {
                   CuiHelper.DestroyUi(player, containerName); 
                }
                playerTimerCUIs.Remove(player);
            }
        }
        private void OnServerInitialized()
        {
            LoadConfig();
            LoadData();
            SetOutpostPos();

            // Initial Unsubscribes
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnPlayerDeath));
            Unsubscribe(nameof(OnUserDisconnected));
            Unsubscribe(nameof(OnNpcTarget));
            Unsubscribe(nameof(CanMountEntity));
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) {
                // Prevent server commands from being run by temporary admins
                // Note: temp admin is required to use ddraw on the client side
                if(isTmpAdmin(player.userID.ToString())) {
                    Puts(_("cheatWarn1", "", player, arg.cmd.FullName));
                    return false; 
                }
            }

            return null;
        }
        #endregion

        public class Configuration
        {
            [JsonProperty("Enabled")]
            public bool enabled { get; private set; } = true;

            [JsonProperty("Event Run Time (Minutes)")]
            public int eventRunTime { get; private set; } = 15;

            [JsonProperty("Player Selection")]
            public string playerSelection { get; private set; } = "random";

            [JsonProperty("Hunted Warmup Time (Seconds)")]
            public float huntedWarmupSeconds { get; private set; } = 90;

            [JsonProperty("No Duplicates")]
            public bool noDuplicates { get; private set; } = true;

            [JsonProperty("No Friendly Kills")]
            public bool noFriendlyKills { get; private set; } = true;

            [JsonProperty("No Animals")]
            public bool noAnimals { get; private set; } = true;

            [JsonProperty(PropertyName = "Disable Vehicles", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> disableVehicles { get; private set; } = new List<string>
            {
	            "air", "water", "car", "animal"
            };

            [JsonProperty("Prize Amount (Server Rewards)")]
            public int prizeServerRewards { get; private set; } = 0;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();
            }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); }

            if (config == null)
            {
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig() => config = new Configuration();

        public class StoredData
        {
            public bool InProgress { get; set; } = false;
            public bool IsStarting { get; set; } = false;
            public string EndTime { get; set; } = "";
            public Dictionary<string, string> CurrentHunted { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, string> CurrentHunters { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, int> Penalty { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, Dictionary<string, Dictionary<string, int>>> Stats { get; set; } = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
            public Dictionary<string, string> LastHunted { get; set; } = new Dictionary<string, string>();
            public StoredData() { }
        }
        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); }

            if (data == null)
            {
                data = new StoredData();
            }
        }
        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, data, true);
        }


        public void SetOutpostPos() 
        {
            string compoundName = "assets/bundled/prefabs/autospawn/monument/medium/compound.prefab";

            var outpostMonument = TerrainMeta.Path.Monuments.Where(z => z.name == compoundName).FirstOrDefault();
            outpostPos = outpostMonument.transform.position; 
        }

        private void addHunted(BasePlayer player) {
           data.CurrentHunted.Add(player.userID.ToString(), player.displayName);
           SaveData();
        }

        private void addHunter(BasePlayer player) {
           data.CurrentHunters.Add(player.userID.ToString(), player.displayName);
           SaveData();
        }

        public bool isHunted(BasePlayer player) => data.CurrentHunted.ContainsKey(player.userID.ToString());
        public bool isHunter(BasePlayer player) => data.CurrentHunters.ContainsKey(player.userID.ToString());

        object OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (player == null || info == null) return null;
            var hunter = info.Initiator as BasePlayer;

            /*if (isHunted(player.userID.ToString()) && hunter != null) 
			{
               // TODO: Stats on damage 
			}*/
            return null;
        }

        private object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (data.InProgress || data.IsStarting) {
                if (player == null || info == null || info.InitiatorPlayer == null) return null;
                if (!info.InitiatorPlayer is BasePlayer) return null;
                var initiator = info.InitiatorPlayer as BasePlayer;

                // Team check
                if (config.noFriendlyKills && isHunted(player) && sameTeam(player, initiator) == true ) {
                    player.health = 1f;
                    return true;
                }

                if (isHunted(player) && (initiator == null || !isHunter(initiator))) {
                    runEventWrapup("interfereddeath");
                    return null;
                }

                if (isHunted(player) && isHunter(initiator)) {
                    runEventWrapup("death", initiator.userID.ToString());
                    return null;
                }
                if (isHunter(player) && isHunted(initiator)) {
                    PrintToChat(_("hunterKillBounty", "", player.displayName));
                    return null;
                }
            }

            return null;
        }

        void OnUserDisconnected(IPlayer player)
        {
            if (data.InProgress || data.IsStarting) {
                if (isHunted(BasePlayer.FindByID(Convert.ToUInt64(player.Id)))) {
                    runEventWrapup("hunteddisconnected");
                    return;
                }
            }
        }
        
        //private void OnPlayerRespawned(BasePlayer player) => OnPlayerConnected(player);

        object OnNpcTarget(BaseAnimalNPC animal, BasePlayer target)
        {
            if (data.InProgress || data.IsStarting) {
                if (config.noAnimals || animal == null || target == null) return null;

                if (isHunted(target))
                {
                    return true;
                }
            }

            return null;
        }

        object CanMountEntity(BasePlayer player, BaseMountable entity)
        
        {
            // TODO: return if not network player
            if (data.InProgress || data.IsStarting) {
                if (isHunted(player) && !mountAllowed(entity)) {
                    return false;
                }
            }
            return null;
        }

        public Dictionary<string, List<string>> allVehicleTypes = new Dictionary<string, List<string>> {
            {"air", new List<string>{"attackhelidriver", "attackheligunner", "transporthelicopilot", "transporthelipilot", "minihelipassenger", "miniheliseat"}},
            {"water", new List<string>{"rhibdriver", "smallboatpassenger", "smallboatdriver", "tugboatdriver"}},
            {"car", new List<string>{"modularcardriverseat", "modularcarpassengerseatright"}},
            {"animal", new List<string>{"saddletest"}}
        };
        public bool mountAllowed(BaseMountable entity) 
        {
            if (config.disableVehicles.Count > 0) {
                foreach(string disabled in config.disableVehicles) { 
                    if (allVehicleTypes[disabled].Contains(entity.ShortPrefabName)) { return false; }
                }
            }

            return true;
        }

        public static void TryInvokeMethod(Action action)
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
               // 
            }
        }

        #region gui
        // Show Banner
        private string color_winner_gold = "0.86 0.71 0.22 0.90";
        private string color_warning_red = "0.65 0.18 0.20 0.80";
        private Timer bannerTimer = null;
        //public Dictionary<BasePlayer, string> playerTimerCUI = new Dictionary<BasePlayer, string>();
        public Dictionary<BasePlayer, List<string>> playerTimerCUIs = new Dictionary<BasePlayer, List<string>>();

        private void updatePlayerUIs(BasePlayer player, string containerName)
        {
            if (playerTimerCUIs.ContainsKey(player))
            {
                playerTimerCUIs[player].Add(containerName);
            }
            else
            {
                playerTimerCUIs.Add(player, new List<string>{containerName});
            }
        }

        private void removePlayerUIs(BasePlayer player, string containerName)
        {
            if (playerTimerCUIs.ContainsKey(player))
            {
                playerTimerCUIs[player].Remove(containerName);
            }
        }
        private void createTimedUi(string displayTo, float seconds, CuiElementContainer container, string containerName)
        {
            // displayTo: all, admin, <steamIDstring>
            if (displayTo != "admin" && displayTo != "all") {
                BasePlayer singlePlayer = BasePlayer.FindByID(Convert.ToUInt64(displayTo));
                CuiHelper.AddUi(singlePlayer, container);
                updatePlayerUIs(singlePlayer, containerName);
            } else {
                foreach(BasePlayer player in BasePlayer.activePlayerList) { 
                    if (displayTo != "admin" && displayTo != "all") break;

                    if (displayTo == "admin" && !player.IsAdmin) continue;

                    CuiHelper.AddUi(player, container);
                    updatePlayerUIs(player, containerName);
                }
            }
            

            // remove the UI after X seconds
            bannerTimer = timer.Once(seconds, () => TryInvokeMethod(() => {
                //DateTime currentTime = DateTime.Now;
                if (displayTo != "admin" && displayTo != "all") {
                    string cuiElement;
                    BasePlayer singlePlayer = BasePlayer.FindByID(Convert.ToUInt64(displayTo));
                   if (playerTimerCUIs.ContainsKey(singlePlayer))
                    {
                        CuiHelper.DestroyUi(singlePlayer, containerName);
                        removePlayerUIs(singlePlayer, containerName);
                    }
                } else {
                    foreach(BasePlayer player in BasePlayer.activePlayerList) { 
                        if (displayTo != "admin" && displayTo != "all") break;

                        if (displayTo == "admin" && !player.IsAdmin) continue;

                        string cuiElement;
                        if (playerTimerCUIs.ContainsKey(player))
                        {
                            CuiHelper.DestroyUi(player, containerName);
                            removePlayerUIs(player, containerName);
                        }
                    }
                }
            }));
        }
        private void removeTimedUi(string containerName, BasePlayer player) 
        {
            CuiHelper.DestroyUi(player, containerName);
            timer.Destroy(ref bannerTimer);
            bannerTimer = null;
            string cuiElement;
            if (playerTimerCUIs.ContainsKey(player))
            {
                CuiHelper.DestroyUi(player, containerName);
                removePlayerUIs(player, containerName);
            }
            return;
        }
        private CuiElementContainer createBanner(string bannerName, string displayText, string bannerColor, string offset = "0.60 .85") 
        {
            var bannerContainer = new CuiElementContainer();
            var bannerPanel = bannerContainer.Add(new CuiPanel
            {
                Image = {
                    Color = bannerColor
                },
                RectTransform = {
                    // <left box position> <bottom edge of box>
                    AnchorMin = "0.40 0.80",
                    // <right edge> <top edge>
                    AnchorMax = offset
                },
                CursorEnabled = false
            }, "Overlay", bannerName);

            var label = bannerContainer.Add(new CuiLabel {
                RectTransform = {
                    AnchorMin = "0.0 0.0",
                    AnchorMax = "1.0 1.0"
                },
                Text = {
                    Text = displayText,
                    FontSize = 15,
                    Color = "1 1 1 1",
                    Align = TextAnchor.MiddleCenter
                }
            }, bannerPanel);

            return bannerContainer;
        }
        private void renderBanner(BasePlayer player, string Message)
        {
            var bannerContainer = new CuiElementContainer();

            var bannerPanel = bannerContainer.Add(new CuiPanel
            {
                Image = {
                    Color = color_winner_gold
                },
                RectTransform = {
                    // <left box position> <bottom edge of box>
                    AnchorMin = "0.40 0.80",
                    // <right edge> <top edge>
                    AnchorMax = "0.60 .85"
                },
                CursorEnabled = false
            }, "Overlay", "ManhuntBanner");

            var label = bannerContainer.Add(new CuiLabel {
                RectTransform = {
                    AnchorMin = "0.0 0.0",
                    AnchorMax = "1.0 1.0"
                },
                Text = {
                    Text = Message,
                    FontSize = 15,
                    Color = "1 1 1 1",
                    Align = TextAnchor.MiddleCenter
                }
            }, bannerPanel);

            CuiHelper.AddUi(player, bannerContainer);
            //playerTimerCUI.Add(player, bannerPanel);
            updatePlayerUIs(player, bannerPanel);
        }
        #endregion

        #region map class
        public class Radar : MonoBehaviour
        {
            private EntityType entityType;
            private int _inactiveSeconds;
            private int activeSeconds;
            public float invokeTime { get; set; }
            public float maxDistance;
            private Vector3 position;
            private float currDistance;
            private int checks;

            public VendingMachineMapMarker vending { get; set; }
            
            
            public MapMarkerGenericRadius generic { get; set; }

            public void CreateGenericMarker(BasePlayer player)
            {
                generic = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", player.transform.position).GetComponent<MapMarkerGenericRadius>();
                if (generic != null) {
                    generic.color1 = Color.red;
                    generic.color2 = Color.blue;
                    generic.radius = 0.35f;
                    generic.alpha = 0.75f;
                    generic.enableSaving = false;
                    generic.Spawn();
                    generic.SendUpdate(); // needed to actually draw the circle
                }
            }

            public void destroyMarker()
            {
                if (vending != null) {
                    vending.Kill(BaseNetworkable.DestroyMode.None);
                }
                if (generic != null) { 
                    generic.Kill(BaseNetworkable.DestroyMode.None);
                }
            }
        }
        #endregion

        #region setup event
        public void startEvent()
        {
            initializeEvent();
        }

        private void initializeEvent()
        {
            debugMsg(_("debugEventInit", ""));
            DateTime validEndTime;
            bool etCheckValid = DateTime.TryParseExact(data.EndTime, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out validEndTime);
            if ((data.InProgress == true || data.IsStarting) == true && etCheckValid) {
                DateTime dtNow = DateTime.Now;
                int remainingSeconds = Convert.ToInt32((validEndTime - dtNow).TotalSeconds);

                // Check for stale event and reset if needed
                if (remainingSeconds <= 0) {
                    runEventWrapup("inittimer");
                    runEventStartUp();
                } else {
                    Puts(_("logAlreadyStarted", ""));
                }
                return;
            }

            runEventStartUp();
        }

        private void selectHunted()
        {
            switch (config.playerSelection) {
                case "random":
                    ListHashSet<BasePlayer> selectionList;
                    if (config.noDuplicates == true) {
                        selectionList = filteredActiveNoLastHunted();
                        // Fallback
                        if (selectionList.Count == 0) {
                            selectionList = BasePlayer.activePlayerList;
                        }
                    } else {
                        selectionList = BasePlayer.activePlayerList;
                    }
                    data.CurrentHunted.Clear();
                    var random = new System.Random();
                    int index = random.Next(selectionList.Count);
                    BasePlayer selectedPlayer = selectionList[index] as BasePlayer;
                    data.CurrentHunted.Add(selectedPlayer.userID.ToString(), selectedPlayer.displayName);
                    SaveData();
                    return;
                
                default:
                    return;
            }
        }

        private bool isLastHunted(string playerID)
        {
            return data.LastHunted.ContainsKey(playerID);
        }

        private ListHashSet<BasePlayer> filteredActiveNoLastHunted()
        {
            ListHashSet<BasePlayer> filtered = new ListHashSet<BasePlayer>();
            foreach (var aPlayer in BasePlayer.activePlayerList)
            {
                if(isLastHunted(aPlayer.userID.ToString()))
                {
                    continue;
                }
                filtered.Add(aPlayer);
            }

            return filtered;
        }
        
        private Timer warmupTimer = null;
        private Timer initEventTimersTimer = null;
        private Timer eventTimer = null;
        private Timer notificationTimer = null;
        private void runEventStartUp()
        {
            data.IsStarting = true;
            SaveData();

            // Check if we have enough players for the event
            // TODO: consider skipping sleeping players
            if (BasePlayer.activePlayerList.Count < 2)
            {
                Puts("Not enough players for Manhunt, skipping.");
                return;
            }

            // Display event alert
            CuiElementContainer container = createBanner("StartSoon", _("notifySoonStart", ""), color_warning_red);
            PrintToChat(_("chatSoonStart", ""));
            createTimedUi("all", 5, container, "StartSoon");

            // Select hunted
            selectHunted();

            if (data.CurrentHunted.Count > 0) {
                // Banners
                CuiElementContainer warmupNotify = createBanner("ManhuntWarmupNotify", _("notifyHunted", ""), color_warning_red);
                CuiElementContainer startNotify = createBanner("ManhuntStartNotify", _("notifyStarted", ""), color_warning_red);

                // notify hunted
                timer.Once(10f, () =>
                {
                    foreach (var x in data.CurrentHunted)
                    {
                        createTimedUi(x.Key, 5, warmupNotify, "ManhuntWarmupNotify");
                    }
                });

                // Start after warmup
                if (config.huntedWarmupSeconds > 0) {
                    warmupTimer = timer.Once(config.huntedWarmupSeconds, () =>
                    {
                        // notify event started
                        createTimedUi("all", 5, startNotify, "ManhuntStartNotify");
                        PrintToChat(_("chatStarted", ""));
                        initEventTimersTimer = timer.Once(15f, () => { runEventTimers();});
                    });
                } else {
                    // Starting right away
                    createTimedUi("all", 5, startNotify, "ManhuntStartNotify"); 
                    initEventTimersTimer = timer.Once(15f, () => { runEventTimers();});
                    
                }

                // Subscribes
                Subscribe(nameof(OnEntityTakeDamage));
                Subscribe(nameof(OnPlayerDeath));
                Subscribe(nameof(OnUserDisconnected));
                Subscribe(nameof(OnNpcTarget));
                Subscribe(nameof(CanMountEntity));
            }

        }

        private void runEventTimers()
        {
            debugMsg("Timers called.");
            var seconds = 60 * config.eventRunTime;
            // Set endTime
            DateTime dateTime = DateTime.Now;
            data.EndTime = dateTime.AddMinutes(config.eventRunTime).ToString("yyyy-MM-dd HH:mm:ss");
            data.InProgress = true;
            data.IsStarting = false;
            SaveData();

            // Handles calling the running event UI
            eventTimer = timer.Once(seconds, () => {
                // Check if event should be over
                runEventWrapup("timer");
            });

            // Set hostile hunted
            foreach (var hn in data.CurrentHunted)
            {
                BasePlayer hostileHunted = BasePlayer.FindByID(Convert.ToUInt64(hn.Key));

                // Move hunted out of safe zones
                safeEject(hostileHunted);

                // Mark hunted hostile for duration of event
                float hostileTime = config.eventRunTime * 60f;
                hostileHunted.MarkHostileFor(hostileTime);
            }

            Action notifyTimeFunc = () => {
                var mri = new Radar();
                var huntedPositions = new List<Vector3>();

                CuiElementContainer uavHuntedNotify = createBanner("UAVNotifyHunted", _("notifyHuntedUAV", ""), color_warning_red);
                CuiElementContainer uavHuntersNotify = createBanner("UAVNotifyHunters", _("notifyHunterUAV", ""), color_warning_red);
                foreach (var d in data.CurrentHunted)
                {
                    BasePlayer thisHunted = BasePlayer.FindByID(Convert.ToUInt64(d.Key));
                    safeEject(thisHunted);
                    huntedPositions.Add(thisHunted.transform.position);

                    // Display map marker
                    //mri.CreateVendorMarker(player);
                    mri.CreateGenericMarker(thisHunted);

                    // Display notification
                    createTimedUi(d.Key, 5, uavHuntedNotify, "UAVNotifyHunted");
                    SendReply(thisHunted, _("chatNextUAV", ""));
                }
                foreach (var s in data.CurrentHunters)
                {
                    BasePlayer thisHunter = BasePlayer.FindByID(Convert.ToUInt64(s.Key));
                    // Display beacon
                    foreach (var hPos in huntedPositions)
                    {
                        Vector3 tpos = hPos;
                        //thisHunter.SendConsoleCommand("ddraw.sphere", 30f, Color.red, tpos, 30f);
                        runAsAdmin(thisHunter, () => {
                           thisHunter.SendConsoleCommand("ddraw.text", 30f, "#229954", tpos, $"<size={80}>⊕</size>"); 
                        });
                        //thisHunter.SendConsoleCommand("ddraw.text", 30f, "#229954", tpos, $"<size={80}>⊕</size>");
                    }

                    // Display notification
                    createTimedUi(s.Key, 5, uavHuntersNotify, "UAVNotifyHunters");
                    SendReply(thisHunter, _("chatNextUAV", ""));
                }

                timer.Once(30f, () => { mri.destroyMarker();});
            };

            // Initial notification and radar
            notifyTimeFunc();
            // Timer to display the notifications
            notificationTimer = timer.Repeat(90f, 0, notifyTimeFunc);

            // End
        }

        private void runEventWrapup(string endedBy, [ Optional ] string playerId)
        {
            debugMsg(_("debugWrapup", "", endedBy));
            data.InProgress = false;
            data.IsStarting = false;
            data.EndTime = "";
            //eventTimer = null;
            bannerTimer = null;
            timer.Destroy(ref warmupTimer);
            timer.Destroy(ref initEventTimersTimer);
            timer.Destroy(ref notificationTimer);
            // Debug: destroy event timer, believe it is firing after wrapup
            timer.Destroy(ref eventTimer);
            SaveData();

            // Determine winners
            switch (endedBy)
            {
                case "death":
                    // Hunted died, award killer
                    if (data.CurrentHunters.ContainsKey(playerId)) {
                        BasePlayer thisHunter = BasePlayer.FindByID(Convert.ToUInt64(playerId));
                        CalculateRewards(thisHunter);
                    }
                    break;
                case "interfereddeath":
                    // Hunted died to outside forces
                    PrintToChat(_("chatOutsideForces", ""));
                    break;
                case "hunteddisconnected":
                    // Hunted disconnected
                    // TODO: Penalty
                    PrintToChat(_("chatHuntedDisco", ""));
                    break;
                case "timer":
                    // Event time ran out, award Hunted
                    foreach (var d in data.CurrentHunted) {
                        BasePlayer thisHunted = BasePlayer.FindByID(Convert.ToUInt64(d.Key));
                        CalculateRewards(thisHunted);
                    }
                    break;
                case "command":
                    // Cancelled by command
                    break;
                case "inittimer":
                    // Cancelled by new event starting
                    break;
            }

            // Clear hostile hunted
            foreach (var hn in data.CurrentHunted)
            {
                BasePlayer hostileHunted = BasePlayer.FindByID(Convert.ToUInt64(hn.Key));

                // Mark hunted hostile for duration of event
                //hostileHunted.State.unHostileTimestamp = DateTime.Now; 
                hostileHunted.MarkHostileFor(0);
            }

            PrintToChat(_("chatEventEnded", ""));
            data.LastHunted.Clear();
            foreach (KeyValuePair<string, string> hEntry in data.CurrentHunted)
            {
                data.LastHunted.Add(hEntry.Key, hEntry.Value);
            }
            data.CurrentHunters.Clear();
            data.CurrentHunted.Clear();
            SaveData();

            // Destroy lingering UIs after award notifications
            timer.Once(5f, () => TryInvokeMethod(() => {
                foreach (var puis in BasePlayer.activePlayerList)
                {
                    DestroyUI(puis);
                }
            }));

            // Unsubscribes
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnPlayerDeath));
            Unsubscribe(nameof(OnUserDisconnected));
            Unsubscribe(nameof(OnNpcTarget));
            Unsubscribe(nameof(CanMountEntity));
        }

        private void CalculateRewards(BasePlayer player) {
            CuiElementContainer winnerNotify = createBanner("ManhuntAwardNotify", _("notifyWinner", "", player.displayName), color_winner_gold);
            // Clear banners that might overlap final notification
            foreach (var puis in BasePlayer.activePlayerList)
            {
                DestroyUI(puis);
            }
            createTimedUi("all", 5, winnerNotify, "ManhuntAwardNotify");
            PrintToChat(_("chatWinner", "", player.displayName));

            if (config.prizeServerRewards > 0)
            {
                PrintToChat(_("chatAwarded", "", player.displayName, config.prizeServerRewards));
                ServerRewards?.Call("AddPoints", player.userID, (int)config.prizeServerRewards);
            }
        }

        private void joinHunt(BasePlayer player)
        {
            // Check for warmup or active event
            if (!data.IsStarting && !data.InProgress) {
               SendReply(player, _("chatNoActiveEvent", player.userID.ToString()));
               return; 
            }

            if (isHunted(player) || isHunter(player)) return;

            if (config.noFriendlyKills && Clans.IsLoaded )
            {
                var p1Tag = Clans?.CallHook("GetClanOf", player.userID);
                if (p1Tag != null) {
                    foreach (var hted in data.CurrentHunted)
                    {
                        if (p1Tag == Clans?.CallHook("GetClanOf", hted.Key)) {
                            SendReply(player, _("chatNoHuntTeam", player.userID.ToString()));
                            return;
                        }
                    }
                }
            }

            // Add them to the hunt
            addHunter(player);
            SendReply(player, _("chatYouJoined", player.userID.ToString()));
            foreach (var x in BasePlayer.activePlayerList)
            {
                if (x.userID.ToString() == player.userID.ToString()) continue;

                PrintToChat(x, _("chatNewJoin", "", player.displayName));
            }
        }

        #endregion

        private void runAsAdmin(BasePlayer player, Action action)
        {
            if (possibleCheatCheck(player))
            {
                return;
            }

            // remember original player state
            bool originalAdminState = player.IsAdmin;

            if (!originalAdminState)
            {
                tmpAdmin.Add(player.userID.ToString(), player.displayName);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }
            try
            {
                action();
            }
            finally
            {
                if (!originalAdminState)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                    tmpAdmin.Remove(player.userID.ToString());
                }
            }
        }

        private static bool possibleCheatCheck(BasePlayer player)
        {
            if (player.IsAdmin || player.IsDeveloper) {
                return false;
            }
            if (player.IsFlying)
            {
                return true;
            } else {
                return false;
            }
        }

        public bool isTmpAdmin(string playerID)
        {
            if (tmpAdmin.ContainsKey(playerID)) {
                return true;
            } else {
                return false;
            }
        }

        private void safeEject(BasePlayer player)
        {
            Vector3 target = outpostPos; 

            if (player.InSafeZone()) {
                Vector3 a = player.transform.position; 
                var ejectMask = Layers.Mask.World | Layers.Mask.Terrain | Layers.Mask.Default | Layers.Mask.Deployed | Layers.Mask.Tree;

                var ejectTo = ((a.XZ3D() - target.XZ3D()).normalized * (300)) + target;
                float y = TerrainMeta.HighestPoint.y + 250f;

                RaycastHit hit;
                if (Physics.Raycast(ejectTo + new Vector3(0f, y, 0f), Vector3.down, out hit, Mathf.Infinity, ejectMask, QueryTriggerInteraction.Ignore))
                {
                    ejectTo.y = hit.point.y + 0.75f;
                }
                else ejectTo.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(ejectTo), TerrainMeta.WaterMap.GetHeight(ejectTo)) + 0.75f;

                player.Teleport(ejectTo);
                player.SendNetworkUpdateImmediate();
            }

        }

        private bool sameTeam(BasePlayer p1, BasePlayer p2)
        {
            if (p1.Team != null) 
            { 
                if (p1.Team.members.Contains(p2.userID)) return true; 
            }
            if (Clans.IsLoaded)
            {
                var p1Tag = Clans?.CallHook("GetClanOf", p1.userID);
                var p2Tag = Clans?.CallHook("GetClanOf", p2.userID);
                if (p1Tag != null && p1Tag == p2Tag) return true;
            }
            return false;
        }

        private void debugMsg(string msg)
        {
            Puts(msg);
        }

        [ChatCommand("manhunt")]
        private void cmdChatActions(BasePlayer player, string command, string[] args)
        {
            if (!config.enabled)
            {
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, _("chatCommandListHead", player.userID.ToString()));
                SendReply(player, "/manhunt join");
                if (permission.UserHasPermission(player.userID.ToString(), "manhunt.admin")) {
                    SendReply(player, "/manhunt start");
                    SendReply(player, "/manhunt end");
                }
                return;
            }

            if (args[0].ToLower() !="join" && !permission.UserHasPermission(player.userID.ToString(), "manhunt.admin")) {
                SendReply(player, _("needPerms", player.userID.ToString()));
                return;
            }

            switch (args[0].ToLower())
            {
                case "start":
                    startEvent();
                    return;
                case "end":
                    runEventWrapup("command");
                    return;
                case "join":
                    joinHunt(player);
                    return;
                case "debug":
                    if (sameTeam(player, BasePlayer.FindByID(Convert.ToUInt64("76561198796415253")))) {
                        Puts("SAME TEAM");
                    } else {
                        Puts("NOT SAME TEAM");
                    }
                    return;
                default:
                    break;
            }

        }

        [ConsoleCommand("mhunt")]
        private void ccmdActions(ConsoleSystem.Arg arg)
        {
            if (!config.enabled)
            {
                return;
            }
            if (arg.Args.Length > 0) {
                switch (arg.Args[0].ToLower()) {
                    case "start":
                        startEvent();
                        break;
                    case "end":
                        runEventWrapup("command");
                        break;
                }
            }
        }

        private string _(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cheatWarn1"] = "Possible cheat attempt by: {0} ran command with temp admin {1}",
                ["hunterKillBounty"] = "A bounty target has killed the hunter {0}",
                ["vendorMarkerText"] = "Bounty last position",
                ["debugEventInit"] = "Init event called.",
                ["debugWrapup"] = "Wrapup called. {0}",
                ["logAlreadyStarted"] = "Event already in progress",
                ["notifySoonStart"] = "Manhunt is about to begin!",
                ["chatSoonStart"] = "Manhunt is about to begin! Join with: /manhunt join",
                ["notifyHunted"] = "You are being hunted!",
                ["notifyStarted"] = "Manhunt started!",
                ["chatStarted"] = "ManHunt event has started! Join with: /manhunt join",
                ["notifyHuntedUAV"] = "A UAV has revealed your location!",
                ["notifyHunterUAV"] = "A UAV has revealed the target!",
                ["chatNextUAV"] = "Next UAV fly over in 90 seconds.",
                ["chatOutsideForces"] = "Outside forces killed bounty.",
                ["chatEventEnded"] = "ManHunt event has ended!",
                ["notifyWinner"] = "{0} has won the Manhunt!",
                ["chatWinner"] = "{0} has won the Manhunt!",
                ["chatAwarded"] = "{0} has been awarded {1} RP!",
                ["chatNoActiveEvent"] = "There is no active Manhunt event.",
                ["chatYouJoined"] = "You have joined the hunt!",
                ["chatNewJoin"] = "{0} has joined the hunt!",
                ["needPerms"] = "Insufficient permissions.",
                ["chatNoHuntTeam"] = "You can not hunt your team members.",
                ["chatHuntedDisco"] = "Hunted disconnected.",
                ["chatCommandListHead"] = "ManHunt Commands:",
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cheatWarn1"] = "Posible intento de trampa por parte de: {0} ejecutó el comando con el administrador temporal {1}",
                ["hunterKillBounty"] = "Un objetivo de recompensa ha matado al cazador {0}",
                ["vendorMarkerText"] = "Objetivo de recompensa en la última posición",
                ["debugEventInit"] = "Evento inicializado llamado.",
                ["debugWrapup"] = "Terminar llamado. {0}",
                ["logAlreadyStarted"] = "Evento ya en progreso",
                ["notifySoonStart"] = "La caza humana está a punto de comenzar!",
                ["chatSoonStart"] = "¡ManHunt está a punto de comenzar! Unirse con: /manhunt join",
                ["notifyHunted"] = "estas siendo cazado!",
                ["notifyStarted"] = "La caza humana comenzó!",
                ["chatStarted"] = "¡El evento ManHunt ha comenzado! Unirse con: /manhunt join",
                ["notifyHuntedUAV"] = "Un UAV ha revelado tu ubicación!",
                ["notifyHunterUAV"] = "Un UAV ha revelado el objetivo.",
                ["chatNextUAV"] = "El próximo UAV sobrevuela en 90 segundos.",
                ["chatOutsideForces"] = "Fuerzas externas mataron a Bouty.",
                ["chatEventEnded"] = "El evento ManHunt ha finalizado!",
                ["notifyWinner"] = "{0} ha ganado el evento!",
                ["chatWinner"] = "{0} ha ganado el evento!",
                ["chatAwarded"] = "{0} ha recibido {1} RP!",
                ["chatNoActiveEvent"] = "No hay ningún evento activo..",
                ["chatYouJoined"] = "Te has unido a la caza!",
                ["chatNewJoin"] = "{0} se ha unido a la caza!",
                ["needPerms"] = "Permisos insuficientes.",
                ["chatNoHuntTeam"] = "No puedes cazar a los miembros de tu equipo.",
                ["chatHuntedDisco"] = "Objetivo desconectado.",
                ["chatCommandListHead"] = "comandos de chat de caza humana:",
            }, this, "es");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cheatWarn1"] = "Possible tentative de triche de la part de: {0} exécuté la commande avec l'administrateur temporaire {1}",
                ["hunterKillBounty"] = "Une cible de prime a tué le chasseur {0}",
                ["vendorMarkerText"] = "Bounty last position",
                ["debugEventInit"] = "Dernière position de la prime.",
                ["debugWrapup"] = "Conclusion appelée. {0}",
                ["logAlreadyStarted"] = "Événement déjà en cours",
                ["notifySoonStart"] = "La chasse à l'homme est sur le point de commencer!",
                ["chatSoonStart"] = "ManHunt est sur le point de commencer ! Rejoignez-nous avec: /manhunt join",
                ["notifyHunted"] = "Tu es traqué!",
                ["notifyStarted"] = "La chasse à l'homme a commencé!",
                ["chatStarted"] = "L'événement ManHunt a commencé! Rejoignez-nous avec: /manhunt join",
                ["notifyHuntedUAV"] = "Un drone a révélé votre position!",
                ["notifyHunterUAV"] = "Un drone a révélé la cible!",
                ["chatNextUAV"] = "Le prochain drone survolera dans 90 secondes.",
                ["chatOutsideForces"] = "Les forces extérieures ont tué la cible.",
                ["chatEventEnded"] = "l'événement est terminé!",
                ["notifyWinner"] = "{0} a gagné la prime!",
                ["chatWinner"] = "{0} a gagné la prime!",
                ["chatAwarded"] = "{0} a reçu {1} RP!",
                ["chatNoActiveEvent"] = "Il n'y a aucun événement actif.",
                ["chatYouJoined"] = "Vous avez rejoint la chasse!",
                ["chatNewJoin"] = "{0} a rejoint la chasse!",
                ["needPerms"] = "Insufficient permissions.",
                ["chatNoHuntTeam"] = "Vous ne pouvez pas chasser les membres de votre équipe.",
                ["chatHuntedDisco"] = "Cible déconnectée.",
                ["chatCommandListHead"] = "commandes de chat de chasse à l'homme:",
            }, this, "fr");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cheatWarn1"] = "Möglicher Cheat-Versuch von: {0} hat den Befehl mit dem temporären Administrator {1} ausgeführt.",
                ["hunterKillBounty"] = "Ein Kopfgeldziel hat den Jäger {0} getötet",
                ["vendorMarkerText"] = "Letzte Position anvisieren",
                ["debugEventInit"] = "Initialisiertes Ereignis",
                ["debugWrapup"] = "Fertigstellung aufgerufen. {0}",
                ["logAlreadyStarted"] = "Veranstaltung bereits im Gange",
                ["notifySoonStart"] = "Die Fahndung beginnt gleich!",
                ["chatSoonStart"] = "ManHunt beginnt gleich! Trete mit .. Ein: /manhunt join",
                ["notifyHunted"] = "Du wirst gejagt!",
                ["notifyStarted"] = "Die Fahndung hat begonnen!",
                ["chatStarted"] = "Die Fahndungsaktion hat begonnen! Trete mit .. Ein: /manhunt join",
                ["notifyHuntedUAV"] = "Ein UAV hat Ihren Standort ermittelt!",
                ["notifyHunterUAV"] = "Ein UAV hat das Ziel entdeckt!",
                ["chatNextUAV"] = "Nächster UAV-Flug in 90 Sekunden.",
                ["chatOutsideForces"] = "Externe Kräfte töteten Kopfgeld.",
                ["chatEventEnded"] = "Die Fahndungsveranstaltung ist beendet!",
                ["notifyWinner"] = "{0} hat die Fahndung gewonnen!",
                ["chatWinner"] = "{0} hat die Fahndung gewonnen!",
                ["chatAwarded"] = "{0} hat {1} RP erhalten!",
                ["chatNoActiveEvent"] = "Es gibt kein aktives Ereignis.",
                ["chatYouJoined"] = "Sie haben sich der Jagd angeschlossen!",
                ["chatNewJoin"] = "{0} hat sich der Jagd angeschlossen!",
                ["needPerms"] = "Nicht ausreichende Berechtigungen.",
                ["chatNoHuntTeam"] = "Sie können Ihre Teammitglieder nicht jagen.",
                ["chatHuntedDisco"] = "Zielverbindung getrennt.",
                ["chatCommandListHead"] = "Fahndungs-Chat-Befehle:",
            }, this, "de");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cheatWarn1"] = "Возможная попытка мошенничества: {0} запустил команду от временного администратора {1}",
                ["hunterKillBounty"] = "Цель награды убила охотника {0}",
                ["vendorMarkerText"] = "Последняя позиция Баунти",
                ["debugEventInit"] = "Инициализировать вызванное событие.",
                ["debugWrapup"] = "Завершение вызвано. {0}",
                ["logAlreadyStarted"] = "Мероприятие уже в процессе",
                ["notifySoonStart"] = "Охота вот-вот начнется!",
                ["chatSoonStart"] = "охота на человека вот-вот начнется! /manhunt join",
                ["notifyHunted"] = "На вас охотятся!",
                ["notifyStarted"] = "Охота началась!",
                ["chatStarted"] = "Охота на людей началась! Присоединяйтесь к: /manhunt join",
                ["notifyHuntedUAV"] = "БПЛА раскрыл ваше местоположение!",
                ["notifyHunterUAV"] = "БПЛА обнаружил цель!",
                ["chatNextUAV"] = "Следующий БПЛА пролетит через 90 секунд.",
                ["chatOutsideForces"] = "Внешние силы убили Баунти.",
                ["chatEventEnded"] = "Мероприятие завершилось!",
                ["notifyWinner"] = "{0} выиграл охоту!",
                ["chatWinner"] = "{0} выиграл охоту!",
                ["chatAwarded"] = "Пользователь {0} получил {1} RP!",
                ["chatNoActiveEvent"] = "Активного события нет.",
                ["chatYouJoined"] = "Вы присоединились к охоте!",
                ["chatNewJoin"] = "{0} присоединился к охоте!",
                ["needPerms"] = "Недостаточно разрешений.",
                ["chatNoHuntTeam"] = "Вы не можете охотиться на членов своей команды.",
                ["chatHuntedDisco"] = "Цель отключена.",
                ["chatCommandListHead"] = "команды чата для розыска:",
            }, this, "ru");
        }

    }

}