using E3Core.Processors;
using E3Core.Settings;
using E3Core.Utility;
using MonoCore;
using System;
using System.Collections.Generic;
using System.Linq;
using static MonoCore.E3ImGUI;

namespace E3Core.UI.Windows.ControlCenter
{
    public static class ControlCenterWindow
    {
        // Window state
        private static bool _windowInitialized = false;
        private static bool _imguiContextReady = false;
        private static string _windowName = "E3 Control Center";
        private const bool EnableDebugLogging = false;

        // Refresh throttling (don't query TLOs every frame)
        private static long _lastRefresh = 0;
        private static long _refreshInterval = 250; // 250ms = 4 times/sec

        // Cached state data (refreshed at _refreshInterval)
        private static StateCache _cachedState = new StateCache();

        // References
        private static IMQ MQ = E3.MQ;
        private static Logging _log = E3.Log;

        [SubSystemInit]
        public static void Init()
        {
            if (Core._MQ2MonoVersion < 0.36m) return;

            E3ImGUI.RegisterWindow(_windowName, RenderControlCenterWindow);

            EventProcessor.RegisterCommand("/e3cc", (x) =>
            {
                if (Core._MQ2MonoVersion < 0.36m)
                {
                    MQ.Write("E3 Control Center requires MQ2Mono 0.36 or greater");
                    return;
                }
                ToggleWindow();
            }, "Toggle E3 Control Center window");
        }

        [ClassInvoke(Data.Class.All)]
        public static void ProcessControlCenter()
        {
            if (!Core.IsProcessing) return;
            RefreshCachedState();
        }

        public static void ToggleWindow()
        {
            try
            {
                if (!_windowInitialized)
                {
                    _windowInitialized = true;
                    imgui_Begin_OpenFlagSet(_windowName, true);
                }
                else
                {
                    bool open = imgui_Begin_OpenFlagGet(_windowName);
                    imgui_Begin_OpenFlagSet(_windowName, !open);
                }
                _imguiContextReady = true;
            }
            catch (Exception ex)
            {
                _log.Write($"Control Center UI error: {ex.Message}", Logging.LogLevels.Error);
                _imguiContextReady = false;
            }
        }

        private static void RenderControlCenterWindow()
        {
            try
            {
                if (!_imguiContextReady) return;
                if (!imgui_Begin_OpenFlagGet(_windowName)) return;
                if (!Core.IsProcessing) return;

                DebugLog("[CC] Starting render");

                PushCurrentTheme();
                try
                {
                    DebugLog("[CC] Theme pushed, acquiring window");
                    using (var window = ImGUIWindow.Aquire())
                    {
                        DebugLog("[CC] Window acquired, setting size");
                        imgui_SetNextWindowSizeWithCond(600f, 480f, (int)ImGuiCond.FirstUseEver);
                        int flags = (int)(ImGuiWindowFlags.ImGuiWindowFlags_NoCollapse);

                        DebugLog("[CC] Calling window.Begin");
                        if (window.Begin(_windowName, flags))
                        {
                            DebugLog("[CC] Window began successfully");
                            // Safety check for E3 initialization
                            if (!E3.IsInit)
                            {
                                imgui_TextColored(0.9f, 0.2f, 0.2f, 1.0f, "E3 is not initialized yet.");
                                imgui_Text("Please wait for E3 to finish loading.");
                                return;
                            }

                            // Refresh state with throttling
                            try
                            {
                                DebugLog("[CC] Rendering panels");
                                RenderCompactLayout();
                                DebugLog("[CC] Panels rendered successfully");
                            }
                            catch (Exception ex2)
                            {
                                imgui_TextColored(0.9f, 0.2f, 0.2f, 1.0f, $"Panel Error: {ex2.Message}");
                                _log.Write($"[CC] Panel render error: {ex2.Message} - {ex2.StackTrace}", Logging.LogLevels.Error);
                            }
                        }
                        else
                        {
                            DebugLog("[CC] window.Begin returned false");
                        }
                    }
                    DebugLog("[CC] Window disposed");
                }
                catch (Exception ex)
                {
                    _log.Write($"[CC] Error rendering Control Center: {ex.Message} - {ex.StackTrace}", Logging.LogLevels.Error);
                }
                finally
                {
                    PopCurrentTheme();
                    DebugLog("[CC] Theme popped, render complete");
                }
            }
            catch (Exception exOuter)
            {
                _log.Write($"[CC] FATAL outer error: {exOuter.Message} - {exOuter.StackTrace}", Logging.LogLevels.Error);
            }
        }

        private static void DebugLog(string message)
        {
            if (!EnableDebugLogging)
            {
                return;
            }

            _log.Write(message, Logging.LogLevels.Debug);
        }

        private static void RefreshCachedState()
        {
            if (!e3util.ShouldCheck(ref _lastRefresh, _refreshInterval)) return;
            _cachedState.Refresh();
        }

        private static void RenderCompactLayout()
        {
            // Row 1: Core Status and Combat side-by-side
            using (var table = ImGUITable.Aquire())
            {
                if (table.BeginTable("TopRow", 2, (int)ImGuiTableFlags.ImGuiTableFlags_SizingStretchProp, imgui_GetContentRegionAvailX(), 0))
                {
                    imgui_TableSetupColumn("Left", 0, 0.5f);
                    imgui_TableSetupColumn("Right", 0, 0.5f);
                    imgui_TableNextRow();

                    // Left Column - Core Status
                    imgui_TableNextColumn();
                    RenderCompactCoreStatus();

                    // Right Column - Combat
                    imgui_TableNextColumn();
                    RenderCompactCombat();
                }
            }

            imgui_Separator();

            // Row 2: Movement and Network side-by-side
            using (var table = ImGUITable.Aquire())
            {
                if (table.BeginTable("MiddleRow", 2, (int)ImGuiTableFlags.ImGuiTableFlags_SizingStretchProp, imgui_GetContentRegionAvailX(), 0))
                {
                    imgui_TableSetupColumn("Left", 0, 0.5f);
                    imgui_TableSetupColumn("Right", 0, 0.5f);
                    imgui_TableNextRow();

                    // Left Column - Movement
                    imgui_TableNextColumn();
                    RenderCompactMovement();

                    // Right Column - Network
                    imgui_TableNextColumn();
                    RenderCompactNetwork();
                }
            }

            imgui_Separator();

            // Row 3: Action buttons - full width, compact grid
            RenderCompactActions();
        }

        private static void RenderCompactCoreStatus()
        {
            imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Core Status");
            imgui_Separator();

            // Compact status indicators
            RenderCompactRow("Paused:", _cachedState.IsPaused ? "YES" : "NO",
                           _cachedState.IsPaused ? ColorScheme.Bad : ColorScheme.Good);
            RenderCompactRow("Dead:", _cachedState.IsDead ? "YES" : "No",
                           _cachedState.IsDead ? ColorScheme.Bad : ColorScheme.Good);

            // Resources in compact form
            RenderCompactRow("HP:", $"{_cachedState.PctHPs}%", GetHealthColor(_cachedState.PctHPs));
            RenderCompactRow("Mana:", $"{_cachedState.PctMana}%", GetResourceColor(_cachedState.PctMana));
            RenderCompactRow("End:", $"{_cachedState.PctEndurance}%", GetResourceColor(_cachedState.PctEndurance));

            // State flags
            RenderCompactRow("Moving:", _cachedState.IsMoving ? "Yes" : "No", ColorScheme.Neutral);
            RenderCompactRow("Invis:", _cachedState.IsInvis ? "Yes" : "No",
                           _cachedState.IsInvis ? ColorScheme.Warning : ColorScheme.Neutral);
            RenderCompactRow("FD:", _cachedState.IsFD ? "Yes" : "No", ColorScheme.Neutral);
        }

        private static void RenderCompactCombat()
        {
            imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Combat");
            imgui_Separator();

            RenderCompactRow("In Combat:", _cachedState.InCombat ? "YES" : "No",
                           GetCombatColor(_cachedState.InCombat));
            RenderCompactRow("Assisting:", _cachedState.IsAssisting ? "Yes" : "No",
                           _cachedState.IsAssisting ? ColorScheme.Info : ColorScheme.Neutral);
            RenderCompactRow("Target:", _cachedState.AssistTargetName, ColorScheme.Neutral);

            if (_cachedState.InCombat)
            {
                RenderCompactRow("Time:", $"{_cachedState.CombatDuration}s", ColorScheme.Info);
                RenderCompactRow("DPS:", $"{_cachedState.DPS}%/s", ColorScheme.Info);
                RenderCompactRow("TTL:", $"{_cachedState.TTL}s", ColorScheme.Info);
            }
        }

        private static void RenderCompactMovement()
        {
            imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Movement");
            imgui_Separator();

            RenderCompactRow("Following:", _cachedState.Following ? "Yes" : "No",
                           _cachedState.Following ? ColorScheme.Info : ColorScheme.Neutral);
            if (_cachedState.Following)
            {
                RenderCompactRow("  Target:", _cachedState.FollowTarget, ColorScheme.Neutral);
            }

            bool isChasing = !string.IsNullOrEmpty(_cachedState.ChaseTarget) && _cachedState.ChaseTarget != "None";
            RenderCompactRow("Chasing:", isChasing ? "Yes" : "No",
                           isChasing ? ColorScheme.Info : ColorScheme.Neutral);
            if (isChasing)
            {
                RenderCompactRow("  Target:", _cachedState.ChaseTarget, ColorScheme.Neutral);
            }

            RenderCompactRow("Anchor:", _cachedState.AnchorOn ? "ON" : "Off",
                           _cachedState.AnchorOn ? ColorScheme.Info : ColorScheme.Neutral);
            if (_cachedState.AnchorOn)
            {
                RenderCompactRow("  Loc:", _cachedState.AnchorLocation, ColorScheme.Neutral);
            }
        }

        private static void RenderCompactNetwork()
        {
            imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Network/Group");
            imgui_Separator();

            RenderCompactRow("Bots:", _cachedState.ConnectedBots.ToString(),
                           _cachedState.ConnectedBots > 0 ? ColorScheme.Good : ColorScheme.Neutral);
            RenderCompactRow("Group:", _cachedState.GroupCount.ToString(),
                           _cachedState.GroupCount > 0 ? ColorScheme.Good : ColorScheme.Neutral);
            RenderCompactRow("Raid:", _cachedState.RaidCount.ToString(),
                           _cachedState.RaidCount > 0 ? ColorScheme.Good : ColorScheme.Neutral);
        }

        private static void RenderCompactActions()
        {
            imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Quick Actions");
            imgui_Separator();

            const float buttonWidth = 100f;
            const float buttonHeight = 0f; // 0 = auto height

            // Row 1: Core Controls
            if (imgui_ButtonEx(_cachedState.IsPaused ? "Resume" : "Pause", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/e3p");
                // Optimistically update UI state for immediate feedback
                _cachedState.IsPaused = !_cachedState.IsPaused;
            }
            imgui_SameLine();
            if (imgui_ButtonEx(_cachedState.GlobalBroadcastEnabled ? "Global BC: ON" : "Global BC: OFF", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/e3globalbroadcast");
                // Optimistically update UI state for immediate feedback
                _cachedState.GlobalBroadcastEnabled = !_cachedState.GlobalBroadcastEnabled;
            }
            imgui_SameLine();
            if (imgui_ButtonEx("Assist Me", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/assistme");
            }
            imgui_SameLine();
            if (imgui_ButtonEx("Back Off", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/backoff");
            }

            // Row 2: Movement Controls
            if (imgui_ButtonEx(_cachedState.Following ? "Follow Off" : "Follow Me", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand(_cachedState.Following ? "/followoff" : "/followme");
                // Optimistically update UI state for immediate feedback
                _cachedState.Following = !_cachedState.Following;
            }
            imgui_SameLine();
            if (imgui_ButtonEx(_cachedState.AnchorOn ? "Anchor Off" : "Anchor On", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand(_cachedState.AnchorOn ? "/anchoroff" : "/anchoron");
                // Optimistically update UI state for immediate feedback
                _cachedState.AnchorOn = !_cachedState.AnchorOn;
            }
            imgui_SameLine();
            if (imgui_ButtonEx("Chase Me", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/chaseme");
            }
            imgui_SameLine();
            if (imgui_ButtonEx("Move To Me", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/mtm");
            }

            // Row 3: Burn Controls
            if (imgui_ButtonEx("Quick Burn", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/quickburns");
            }
            imgui_SameLine();
            if (imgui_ButtonEx("Full Burn", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/fullburns");
            }
            imgui_SameLine();
            if (imgui_ButtonEx("Long Burn", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/longburns");
            }
            imgui_SameLine();
            if (imgui_ButtonEx("Epic Burn", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/epicburns");
            }

            // Row 4: Additional Actions
            if (imgui_ButtonEx("Swarm Pets", buttonWidth, buttonHeight))
            {
                EventProcessor.ProcessMQCommand("/swarmpets");
            }
        }

        private static void RenderCompactRow(string label, string value, Color color)
        {
            imgui_Text(label);
            imgui_SameLine();
            imgui_TextColored(color.R, color.G, color.B, color.A, value);
        }

        private static void RenderCoreStatusPanel()
        {
            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                 ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed);

                if (tree.TreeNodeEx("Core Status", flags))
                {
                    RenderStatusRow("Paused", _cachedState.IsPaused ? "YES" : "NO",
                                   _cachedState.IsPaused ? ColorScheme.Bad : ColorScheme.Good);
                    RenderStatusRow("HP", $"{_cachedState.PctHPs}%",
                                   GetHealthColor(_cachedState.PctHPs));
                    RenderStatusRow("Mana", $"{_cachedState.PctMana}%",
                                   GetResourceColor(_cachedState.PctMana));
                    RenderStatusRow("End", $"{_cachedState.PctEndurance}%",
                                   GetResourceColor(_cachedState.PctEndurance));
                    RenderStatusRow("Moving", _cachedState.IsMoving ? "Yes" : "No", ColorScheme.Neutral);
                    RenderStatusRow("Invis", _cachedState.IsInvis ? "Yes" : "No",
                                   _cachedState.IsInvis ? ColorScheme.Warning : ColorScheme.Neutral);
                    RenderStatusRow("FD", _cachedState.IsFD ? "Yes" : "No", ColorScheme.Neutral);
                    RenderStatusRow("Dead", _cachedState.IsDead ? "YES" : "No",
                                   _cachedState.IsDead ? ColorScheme.Bad : ColorScheme.Good);
                }
            }
        }

        private static void RenderCombatPanel()
        {
            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                 ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed);

                if (tree.TreeNodeEx("Combat", flags))
                {
                    RenderStatusRow("In Combat", _cachedState.InCombat ? "YES" : "No",
                                   GetCombatColor(_cachedState.InCombat));
                    RenderStatusRow("Assisting", _cachedState.IsAssisting ? "Yes" : "No",
                                   _cachedState.IsAssisting ? ColorScheme.Info : ColorScheme.Neutral);
                    RenderStatusRow("Assist Target", _cachedState.AssistTargetName, ColorScheme.Neutral);

                    if (_cachedState.InCombat)
                    {
                        RenderStatusRow("Combat Time", $"{_cachedState.CombatDuration}s", ColorScheme.Info);
                        RenderStatusRow("DPS", $"{_cachedState.DPS}%/s", ColorScheme.Info);
                        RenderStatusRow("TTL", $"{_cachedState.TTL}s", ColorScheme.Info);
                    }
                }
            }
        }

        private static void RenderMovementPanel()
        {
            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                 ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed);

                if (tree.TreeNodeEx("Movement", flags))
                {
                    RenderStatusRow("Following", _cachedState.Following ? "Yes" : "No",
                                   _cachedState.Following ? ColorScheme.Info : ColorScheme.Neutral);
                    if (_cachedState.Following)
                    {
                        RenderStatusRow("Follow Target", _cachedState.FollowTarget, ColorScheme.Neutral);
                    }

                    RenderStatusRow("Chasing", !string.IsNullOrEmpty(_cachedState.ChaseTarget) && _cachedState.ChaseTarget != "None" ? "Yes" : "No",
                                   !string.IsNullOrEmpty(_cachedState.ChaseTarget) && _cachedState.ChaseTarget != "None" ? ColorScheme.Info : ColorScheme.Neutral);
                    if (!string.IsNullOrEmpty(_cachedState.ChaseTarget) && _cachedState.ChaseTarget != "None")
                    {
                        RenderStatusRow("Chase Target", _cachedState.ChaseTarget, ColorScheme.Neutral);
                    }

                    RenderStatusRow("Anchor", _cachedState.AnchorOn ? "ON" : "Off",
                                   _cachedState.AnchorOn ? ColorScheme.Info : ColorScheme.Neutral);
                    if (_cachedState.AnchorOn)
                    {
                        RenderStatusRow("Anchor Loc", _cachedState.AnchorLocation, ColorScheme.Neutral);
                    }
                }
            }
        }

        private static void RenderNetworkPanel()
        {
            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                 ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed);

                if (tree.TreeNodeEx("Network/Group", flags))
                {
                    RenderStatusRow("Connected Bots", _cachedState.ConnectedBots.ToString(),
                                   _cachedState.ConnectedBots > 0 ? ColorScheme.Good : ColorScheme.Neutral);
                    RenderStatusRow("Group Members", _cachedState.GroupCount.ToString(),
                                   _cachedState.GroupCount > 0 ? ColorScheme.Good : ColorScheme.Neutral);
                    RenderStatusRow("Raid Members", _cachedState.RaidCount.ToString(),
                                   _cachedState.RaidCount > 0 ? ColorScheme.Good : ColorScheme.Neutral);
                }
            }
        }

        private static void RenderActionsPanel()
        {
            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                 ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed);

                if (tree.TreeNodeEx("Quick Actions", flags))
                {
                    // Pause/Combat Controls
                    imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Pause/Combat:");

                    if (imgui_Button(_cachedState.IsPaused ? "Resume" : "Pause"))
                    {
                        EventProcessor.ProcessMQCommand("/e3p");
                    }

                    imgui_SameLine();
                    if (imgui_Button("Assist Me"))
                    {
                        EventProcessor.ProcessMQCommand("/assistme");
                    }

                    imgui_SameLine();
                    if (imgui_Button("Back Off"))
                    {
                        EventProcessor.ProcessMQCommand("/backoff");
                    }

                    imgui_Separator();

                    // Movement Controls
                    imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Movement:");

                    if (imgui_Button(_cachedState.Following ? "Follow Off" : "Follow Me"))
                    {
                        EventProcessor.ProcessMQCommand(_cachedState.Following ? "/followoff" : "/followme");
                    }

                    imgui_SameLine();
                    if (imgui_Button(_cachedState.AnchorOn ? "Anchor Off" : "Anchor On"))
                    {
                        EventProcessor.ProcessMQCommand(_cachedState.AnchorOn ? "/anchoroff" : "/anchoron");
                    }

                    imgui_SameLine();
                    if (imgui_Button("Chase Me"))
                    {
                        EventProcessor.ProcessMQCommand("/chaseme");
                    }

                    if (imgui_Button("Move To Me"))
                    {
                        EventProcessor.ProcessMQCommand("/mtm");
                    }

                    imgui_Separator();

                    // Burn Controls
                    imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, "Burns:");

                    if (imgui_Button("Quick Burn"))
                    {
                        EventProcessor.ProcessMQCommand("/quickburns");
                    }

                    imgui_SameLine();
                    if (imgui_Button("Full Burn"))
                    {
                        EventProcessor.ProcessMQCommand("/fullburns");
                    }

                    imgui_SameLine();
                    if (imgui_Button("Long Burn"))
                    {
                        EventProcessor.ProcessMQCommand("/longburns");
                    }

                    if (imgui_Button("Epic Burn"))
                    {
                        EventProcessor.ProcessMQCommand("/epicburns");
                    }

                    imgui_SameLine();
                    if (imgui_Button("Swarm Pets"))
                    {
                        EventProcessor.ProcessMQCommand("/swarmpets");
                    }
                }
            }
        }

        // Helper functions
        private static void RenderStatusRow(string label, string value, Color color)
        {
            imgui_TextColored(ColorScheme.Label.R, ColorScheme.Label.G, ColorScheme.Label.B, ColorScheme.Label.A, label + ":");
            imgui_SameLine();
            imgui_TextColored(color.R, color.G, color.B, color.A, value);
        }

        private static Color GetHealthColor(int pctHPs)
        {
            if (pctHPs >= 80) return ColorScheme.Good;
            if (pctHPs >= 50) return ColorScheme.Warning;
            return ColorScheme.Bad;
        }

        private static Color GetResourceColor(int pctResource)
        {
            if (pctResource >= 60) return ColorScheme.Good;
            if (pctResource >= 30) return ColorScheme.Warning;
            return ColorScheme.Bad;
        }

        private static Color GetCombatColor(bool inCombat)
        {
            return inCombat ? ColorScheme.Warning : ColorScheme.Good;
        }

        // Color scheme
        private static class ColorScheme
        {
            public static readonly Color Good = new Color(0.2f, 0.9f, 0.3f, 1.0f);      // Green
            public static readonly Color Bad = new Color(0.9f, 0.2f, 0.2f, 1.0f);       // Red
            public static readonly Color Warning = new Color(1.0f, 0.8f, 0.2f, 1.0f);   // Yellow/Orange
            public static readonly Color Neutral = new Color(0.8f, 0.8f, 0.8f, 1.0f);   // Gray
            public static readonly Color Info = new Color(0.6f, 0.8f, 1.0f, 1.0f);      // Light Blue
            public static readonly Color Label = new Color(0.7f, 0.9f, 1.0f, 1.0f);     // Cyan-ish
        }

        private struct Color
        {
            public float R, G, B, A;
            public Color(float r, float g, float b, float a)
            {
                R = r;
                G = g;
                B = b;
                A = a;
            }
        }

        // State cache class
        private class StateCache
        {
            // Core Status
            public bool IsPaused;
            public int PctHPs;
            public int PctMana;
            public int PctEndurance;
            public bool IsMoving;
            public bool IsInvis;
            public bool IsFD;
            public bool IsDead;

            // Combat
            public bool IsAssisting;
            public string AssistTargetName;
            public bool InCombat;
            public long CombatDuration;
            public long DPS;
            public long TTL;

            // Movement
            public bool Following;
            public string FollowTarget;
            public string ChaseTarget;
            public bool AnchorOn;
            public string AnchorLocation;

            // Network
            public int ConnectedBots;
            public int GroupCount;
            public int RaidCount;
            public bool GlobalBroadcastEnabled;

            public void Refresh()
            {
                try
                {
                    // Check if E3 is initialized
                    if (!E3.IsInit || !Core.IsProcessing)
                    {
                        // Set safe defaults
                        IsPaused = true;
                        AssistTargetName = "E3 Not Initialized";
                        FollowTarget = "None";
                        ChaseTarget = "None";
                        AnchorLocation = "Not Set";
                        return;
                    }

                    // Core Status - direct from E3 static properties
                    try
                    {
                        IsPaused = Basics.IsPaused;
                        PctHPs = E3.PctHPs;
                        IsMoving = E3.IsMoving;
                        IsInvis = E3.IsInvis;
                        IsFD = E3.IsFD;
                        IsDead = E3._amIDead;
                    }
                    catch
                    {
                        // Core status failed, use defaults
                        IsPaused = false;
                        PctHPs = 0;
                        IsMoving = false;
                        IsInvis = false;
                        IsFD = false;
                        IsDead = false;
                    }

                    // Query TLOs only once per refresh
                    try
                    {
                        // During ImGui rendering we cannot allow MQ to yield back to C++ mid-frame.
                        // Pass delayPossible: false on TLO reads to avoid the implicit Delay(0) deadlock.
                        PctMana = MQ.Query<int>("${Me.PctMana}", delayPossible: false);
                        PctEndurance = MQ.Query<int>("${Me.PctEndurance}", delayPossible: false);
                    }
                    catch
                    {
                        PctMana = 0;
                        PctEndurance = 0;
                    }

                    // Combat - from Assist processor
                    IsAssisting = Assist.IsAssisting;
                    InCombat = E3.CurrentInCombat;
                    CombatDuration = Assist.CurrentSecondsInCombat;
                    DPS = Assist.MobPctHealthLossPerSecond;
                    TTL = Assist.MobLifeExpectancy;

                    // Get assist target name
                    try
                    {
                        if (Assist.AssistTargetID > 0 && E3.Spawns != null && E3.Spawns.TryByID(Assist.AssistTargetID, out var spawn, false))
                        {
                            AssistTargetName = spawn.Name ?? "Unknown";
                        }
                        else
                        {
                            AssistTargetName = "None";
                        }
                    }
                    catch
                    {
                        AssistTargetName = "Error";
                    }

                    // Movement
                    Following = Movement.Following;
                    FollowTarget = Movement.FollowTargetName ?? "None";
                    ChaseTarget = Movement.ChaseTargetName ?? "None";
                    AnchorOn = Movement.Anchor_X != double.MinValue;
                    if (AnchorOn)
                    {
                        AnchorLocation = $"{Movement.Anchor_X:F0}, {Movement.Anchor_Y:F0}, {Movement.Anchor_Z:F0}";
                    }
                    else
                    {
                        AnchorLocation = "Not Set";
                    }

                    // Network - use readOnly=true to avoid blocking network calls from UI thread
                    try
                    {
                        if (E3.Bots != null)
                        {
                            var bots = E3.Bots.BotsConnected(readOnly: true);
                            ConnectedBots = bots != null ? bots.Count : 0;
                        }
                        else
                        {
                            ConnectedBots = 0;
                        }
                    }
                    catch
                    {
                        ConnectedBots = 0;
                    }

                    GroupCount = Basics.GroupMembers != null ? Basics.GroupMembers.Count : 0;
                    RaidCount = Basics.RaidMembers != null ? Basics.RaidMembers.Count : 0;

                    // Global Broadcast
                    GlobalBroadcastEnabled = SharedDataBots.IsGlobalBroadcastEnabled;
                }
                catch (Exception ex)
                {
                    _log.Write($"Error refreshing Control Center state: {ex.Message}", Logging.LogLevels.Error);
                }
            }
        }
    }
}
