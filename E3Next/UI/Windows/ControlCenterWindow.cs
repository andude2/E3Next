using E3Core.Processors;
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
            if (!_imguiContextReady) return;
            if (!imgui_Begin_OpenFlagGet(_windowName)) return;

            RefreshCachedState();

            PushCurrentTheme();
            try
            {
                using (var window = ImGUIWindow.Aquire())
                {
                    imgui_SetNextWindowSizeWithCond(420f, 650f, (int)ImGuiCond.FirstUseEver);
                    int flags = (int)(ImGuiWindowFlags.ImGuiWindowFlags_NoCollapse);

                    if (window.Begin(_windowName, flags))
                    {
                        RenderCoreStatusPanel();
                        imgui_Separator();
                        RenderCombatPanel();
                        imgui_Separator();
                        RenderMovementPanel();
                        imgui_Separator();
                        RenderNetworkPanel();
                        imgui_Separator();
                        RenderActionsPanel();
                    }
                }
            }
            finally
            {
                PopCurrentTheme();
            }
        }

        private static void RefreshCachedState()
        {
            if (!e3util.ShouldCheck(ref _lastRefresh, _refreshInterval)) return;
            _cachedState.Refresh();
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

            public void Refresh()
            {
                // Core Status - direct from E3 static properties
                IsPaused = Basics.IsPaused;
                PctHPs = E3.PctHPs;
                IsMoving = E3.IsMoving;
                IsInvis = E3.IsInvis;
                IsFD = E3.IsFD;
                IsDead = E3._amIDead;

                // Query TLOs only once per refresh
                PctMana = MQ.Query<int>("${Me.PctMana}");
                PctEndurance = MQ.Query<int>("${Me.PctEndurance}");

                // Combat - from Assist processor
                IsAssisting = Assist.IsAssisting;
                InCombat = E3.CurrentInCombat;
                CombatDuration = Assist.CurrentSecondsInCombat;
                DPS = Assist.MobPctHealthLossPerSecond;
                TTL = Assist.MobLifeExpectancy;

                // Get assist target name
                if (Assist.AssistTargetID > 0 && E3.Spawns.TryByID(Assist.AssistTargetID, out var spawn, false))
                {
                    AssistTargetName = spawn.Name;
                }
                else
                {
                    AssistTargetName = "None";
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

                // Network
                try
                {
                    ConnectedBots = E3.Bots.BotsConnected().Count();
                }
                catch
                {
                    ConnectedBots = 0;
                }

                GroupCount = Basics.GroupMembers != null ? Basics.GroupMembers.Count : 0;
                RaidCount = Basics.RaidMembers != null ? Basics.RaidMembers.Count : 0;
            }
        }
    }
}
