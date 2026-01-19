using E3Core.Data;
using E3Core.Processors;
using E3Core.Server;
using E3Core.Utility;
using MonoCore;
using System;
using System.Collections.Generic;
using System.Linq;
using static MonoCore.E3ImGUI;

namespace E3Core.UI.Windows
{
    public static class E3TasksWindow
    {
        private const string WindowName = "E3 Tasks";
        private const long TimerDisplayThresholdSeconds = 604800; // one week

        private static bool _windowInitialized;
        private static bool _imguiContextReady;
        private static bool _forceRefresh;
        private static long _nextRefresh;
        private static readonly long _refreshInterval = 1000;
        private static long _lastDataUpdate;

        // Cached snapshot of the last task query so the ImGui render stays cheap.
        private static readonly List<TaskSnapshot> _cachedTasks = new List<TaskSnapshot>();
        private static readonly List<PeerTaskSummary> _peerTasks = new List<PeerTaskSummary>();

        private enum PeerSortMode
        {
            ByPeer,
            ByTask
        }

        private static PeerSortMode _peerSortMode = PeerSortMode.ByPeer;

        private static readonly IMQ MQ = E3.MQ;
        private static readonly Logging _log = E3.Log;

        private static readonly (float R, float G, float B) CompletedColor = (0.25f, 0.85f, 0.4f);
        private static readonly (float R, float G, float B) ActiveColor = (0.95f, 0.85f, 0.35f);

        [SubSystemInit]
        public static void Init()
        {
            if (Core._MQ2MonoVersion < 0.36m) return;

            E3ImGUI.RegisterWindow(WindowName, RenderWindow);

            EventProcessor.RegisterCommand("/e3tasks", x =>
            {
                if (Core._MQ2MonoVersion < 0.36m)
                {
                    MQ.Write("E3 Tasks window requires MQ2Mono 0.36 or greater.");
                    return;
                }

                ToggleWindow();
            }, "Toggle the E3 Task progress window");
        }

        private static void RenderPeerPanel(PeerTaskSummary peer)
        {
            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                   ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed |
                                   ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_SpanAvailWidth);

                string header = $"{peer.Name} ({peer.Tasks.Count} tasks)";
                if (tree.TreeNodeEx($"{header}##peer_{peer.Name}", flags))
                {
                    RenderPeerTaskTable(peer.Name, peer.Tasks);
                }
            }
        }

        private static void RenderPeerTaskTable(string peerName, List<TaskWireSummary> tasks)
        {
            if (tasks == null || tasks.Count == 0)
            {
                imgui_TextColored(0.75f, 0.75f, 0.75f, 1f, "No shared tasks yet.");
                return;
            }

            using (var table = ImGUITable.Aquire())
            {
                int tableFlags = (int)(ImGuiTableFlags.ImGuiTableFlags_RowBg |
                                       ImGuiTableFlags.ImGuiTableFlags_BordersInner |
                                       ImGuiTableFlags.ImGuiTableFlags_BordersOuter |
                                       ImGuiTableFlags.ImGuiTableFlags_Resizable);

                if (!table.BeginTable($"PeerTasks##{peerName}", 3, tableFlags, 0f, 0f))
                {
                    return;
                }

                imgui_TableSetupColumn("Task", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthStretch, 260f);
                imgui_TableSetupColumn("Step", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthStretch, 260f);
                imgui_TableSetupColumn("Progress", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthFixed, 140f);
                imgui_TableHeadersRow();

                foreach (var task in tasks
                    .OrderBy(t => t.IsComplete)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
                {
                    imgui_TableNextRow();

                    imgui_TableNextColumn();
                    string taskLabel = string.IsNullOrEmpty(task.Type)
                        ? task.Title
                        : $"{task.Title} [{task.Type}]";
                    imgui_TextWrapped(taskLabel);

                    imgui_TableNextColumn();
                    string step = string.IsNullOrEmpty(task.ActiveStep)
                        ? (task.IsComplete ? "Complete" : "No step info yet")
                        : task.ActiveStep;
                    imgui_TextWrapped(step);

                    imgui_TableNextColumn();
                    string progress;
                    if (task.TotalObjectives > 0)
                    {
                        int completed = Math.Max(0, Math.Min(task.CompletedObjectives, task.TotalObjectives));
                        progress = $"{completed}/{task.TotalObjectives}";
                    }
                    else
                    {
                        progress = task.IsComplete ? "Done" : "0/0";
                    }

                    if (!string.IsNullOrEmpty(task.TimerDisplay))
                    {
                        progress += $" ({task.TimerDisplay})";
                    }

                    var color = task.IsComplete ? CompletedColor : ActiveColor;
                    imgui_TextColored(color.R, color.G, color.B, 1f, progress);
                }
            }
        }

        public static void ToggleWindow()
        {
            try
            {
                if (!_windowInitialized)
                {
                    _windowInitialized = true;
                    imgui_Begin_OpenFlagSet(WindowName, true);
                }
                else
                {
                    bool open = imgui_Begin_OpenFlagGet(WindowName);
                    imgui_Begin_OpenFlagSet(WindowName, !open);
                }

                _imguiContextReady = true;
            }
            catch (Exception ex)
            {
                _log.Write($"E3 Tasks window error: {ex.Message}", Logging.LogLevels.Error);
                _imguiContextReady = false;
            }
        }

        private static void RenderWindow()
        {
            if (!_imguiContextReady) return;
            if (!imgui_Begin_OpenFlagGet(WindowName)) return;

            RefreshTaskData();

            PushCurrentTheme();
            try
            {
                imgui_SetNextWindowSizeWithCond(620f, 680f, (int)ImGuiCond.FirstUseEver);

                using (var window = ImGUIWindow.Aquire())
                {
                    int flags = (int)ImGuiWindowFlags.ImGuiWindowFlags_NoCollapse;
                    if (!window.Begin(WindowName, flags))
                    {
                        return;
                    }

                    using (var tabBar = ImGUITabBar.Aquire())
                    {
                        if (tabBar.BeginTabBar("E3TasksTabs"))
                        {
                            RenderMyTasksTab();
                            RenderPeerTab();
                        }
                    }
                }
            }
            finally
            {
                PopCurrentTheme();
            }
        }

        private static void RenderMyTasksTab()
        {
            using (var tab = ImGUITabItem.Aquire())
            {
                if (!tab.BeginTabItem("My Tasks")) return;

                RenderTaskHeader();
                imgui_Separator();
                RenderTasks();
            }
        }

        private static void RenderPeerTab()
        {
            using (var tab = ImGUITabItem.Aquire())
            {
                if (!tab.BeginTabItem("Peer Overview")) return;

                RenderPeerHeader();
                imgui_Separator();
                RenderPeerOverview();
            }
        }

        private static void RenderTaskHeader()
        {
            imgui_Text($"Active tasks: {_cachedTasks.Count}");
            imgui_SameLine();

            if (imgui_Button("Refresh"))
            {
                _forceRefresh = true;
                RefreshTaskData(force: true);
            }

            imgui_TextColored(0.65f, 0.85f, 1f, 1f, "Track solo and shared task objectives without opening the EQ Task window.");
        }

        private static void RenderPeerHeader()
        {
            imgui_Text($"Peers reporting: {_peerTasks.Count}");

            if (_peerTasks.Count == 0)
            {
                imgui_TextColored(0.75f, 0.75f, 0.75f, 1f,
                    "Waiting for bots to publish task data. Ensure each toon is running the latest build.");
            }
            else if (_peerSortMode == PeerSortMode.ByPeer)
            {
                imgui_TextColored(0.65f, 0.85f, 1f, 1f,
                    "Expand a peer below to see their current task, step text, and progress at a glance.");
            }
            else
            {
                imgui_TextColored(0.65f, 0.85f, 1f, 1f,
                    "Expand a task to see which peers share it and what step each is on.");
            }

            string currentMode = _peerSortMode == PeerSortMode.ByPeer ? "View: By Peer" : "View: By Task";
            using (var combo = ImGUICombo.Aquire())
            {
                imgui_SameLine();
                if (combo.BeginCombo("##PeerView", currentMode))
                {
                    if (imgui_Selectable("View: By Peer", _peerSortMode == PeerSortMode.ByPeer))
                    {
                        _peerSortMode = PeerSortMode.ByPeer;
                    }
                    if (imgui_Selectable("View: By Task", _peerSortMode == PeerSortMode.ByTask))
                    {
                        _peerSortMode = PeerSortMode.ByTask;
                    }
                }
            }
        }

        private static void RenderTasks()
        {
            if (_cachedTasks.Count == 0)
            {
                imgui_TextColored(0.75f, 0.75f, 0.75f, 1f, "No active tasks detected. Accept a task to populate this view.");
                return;
            }

            foreach (var task in _cachedTasks)
            {
                RenderTask(task);
            }
        }

        private static void RenderPeerOverview()
        {
            if (_peerSortMode == PeerSortMode.ByPeer)
            {
                RenderPeerOverviewByPeer();
            }
            else
            {
                RenderPeerOverviewByTask();
            }
        }

        private static void RenderPeerOverviewByPeer()
        {
            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                   ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed);

                if (tree.TreeNodeEx("Peer Task Overview", flags))
                {
                    if (_peerTasks.Count == 0)
                    {
                        imgui_TextColored(0.75f, 0.75f, 0.75f, 1f, "No connected peers have reported task data yet.");
                        return;
                    }

                    foreach (var peer in _peerTasks)
                    {
                        RenderPeerPanel(peer);
                    }
                }
            }
        }

        private static void RenderPeerOverviewByTask()
        {
            var groups = BuildPeerTaskGroups();

            using (var tree = ImGUITree.Aquire())
            {
                int flags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                   ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed);

                if (tree.TreeNodeEx("Tasks Across Peers", flags))
                {
                    if (groups.Count == 0)
                    {
                        imgui_TextColored(0.75f, 0.75f, 0.75f, 1f,
                            "No peer task data to display. Accept a task and share to see progress.");
                        return;
                    }

                    foreach (var group in groups)
                    {
                        string header = string.IsNullOrEmpty(group.Type)
                            ? group.Title
                            : $"{group.Title} [{group.Type}]";
                        header += $" ({group.Entries.Count})";

                        using (var subTree = ImGUITree.Aquire())
                        {
                            if (subTree.TreeNodeEx($"{header}##taskgroup_{group.Key}", flags))
                            {
                                RenderGroupedTaskTable(group);
                            }
                        }
                    }
                }
            }
        }

        private static void RenderTask(TaskSnapshot task)
        {
            using (var tree = ImGUITree.Aquire())
            {
                int treeFlags = (int)(ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_DefaultOpen |
                                      ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_Framed |
                                      ImGuiTreeNodeFlags.ImGuiTreeNodeFlags_SpanAvailWidth);

                string header = string.IsNullOrEmpty(task.Type)
                    ? task.Title
                    : $"{task.Title} [{task.Type}]";

                if (task.IsComplete)
                {
                    header += " (Complete)";
                }

                if (tree.TreeNodeEx($"{header}##task_{task.Slot}", treeFlags))
                {
                    RenderTaskSummary(task);
                    imgui_Separator();
                    RenderObjectivesTable(task);
                }
            }

            imgui_Separator();
        }

        private static void RenderTaskSummary(TaskSnapshot task)
        {
            var statusColor = task.IsComplete ? CompletedColor : ActiveColor;
            imgui_TextColored(statusColor.R, statusColor.G, statusColor.B, 1f,
                $"Status: {(task.IsComplete ? "Complete" : "In Progress")}");

            if (!string.IsNullOrWhiteSpace(task.ActiveStep))
            {
                imgui_TextColored(0.75f, 0.9f, 1f, 1f, "Current Step:");
                imgui_TextWrapped(task.ActiveStep);
            }

            if (task.TimerSeconds > 0 && task.TimerSeconds < TimerDisplayThresholdSeconds && !string.IsNullOrWhiteSpace(task.TimerDisplay))
            {
                imgui_TextColored(0.9f, 0.7f, 0.35f, 1f, $"Time Remaining: {task.TimerDisplay}");
            }

            if (task.MemberCount > 1 || !string.IsNullOrWhiteSpace(task.Leader))
            {
                string summary = string.Empty;
                if (task.MemberCount > 1)
                {
                    summary = $"Members: {task.MemberCount}";
                }

                if (!string.IsNullOrWhiteSpace(task.Leader))
                {
                    summary = string.IsNullOrEmpty(summary)
                        ? $"Leader: {task.Leader}"
                        : $"{summary} | Leader: {task.Leader}";
                }

                if (!string.IsNullOrEmpty(summary))
                {
                    imgui_Text(summary);
                }
            }
        }

        private static void RenderObjectivesTable(TaskSnapshot task)
        {
            if (task.Objectives.Count == 0)
            {
                imgui_TextColored(0.75f, 0.75f, 0.75f, 1f, "No objectives found for this task yet.");
                return;
            }

            bool enableScroll = task.Objectives.Count > 6;
            float desiredHeight = enableScroll
                ? Math.Min(Math.Max(task.Objectives.Count * 26f, 150f), Math.Max(220f, imgui_GetContentRegionAvailY() * 0.6f))
                : 0f;

            int tableFlags = (int)(ImGuiTableFlags.ImGuiTableFlags_RowBg |
                                   ImGuiTableFlags.ImGuiTableFlags_BordersInner |
                                   ImGuiTableFlags.ImGuiTableFlags_BordersOuter |
                                   ImGuiTableFlags.ImGuiTableFlags_Resizable |
                                   (enableScroll ? ImGuiTableFlags.ImGuiTableFlags_ScrollY : 0));

            using (var table = ImGUITable.Aquire())
            {
                if (!table.BeginTable($"TaskObjectives##{task.Slot}", 3, tableFlags, 0f, desiredHeight))
                {
                    return;
                }

                imgui_TableSetupColumn("Objective", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthStretch, 300f);
                imgui_TableSetupColumn("Progress", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthFixed, 110f);
                imgui_TableSetupColumn("Zone", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthFixed, 140f);
                imgui_TableHeadersRow();

                foreach (var objective in task.Objectives)
                {
                    imgui_TableNextRow();

                    imgui_TableNextColumn();
                    string label = $"{objective.Index}. {objective.Instruction}";
                    if (objective.Optional)
                    {
                        label += " (Optional)";
                    }
                    imgui_TextWrapped(label);

                    imgui_TableNextColumn();
                    var color = objective.IsComplete ? CompletedColor : ActiveColor;
                    string statusText = string.IsNullOrWhiteSpace(objective.Status)
                        ? (objective.IsComplete ? "Done" : "In Progress")
                        : objective.Status;
                    imgui_TextColored(color.R, color.G, color.B, 1f, statusText);

                    imgui_TableNextColumn();
                    imgui_Text(string.IsNullOrWhiteSpace(objective.Zone) ? "Any" : objective.Zone);
                }
            }
        }

        private static void RefreshTaskData(bool force = false)
        {
            if (!force)
            {
                if (!_forceRefresh && !e3util.ShouldCheck(ref _nextRefresh, _refreshInterval))
                {
                    return;
                }
            }

            _forceRefresh = false;

            try
            {
                var snapshot = TaskDataCollector.Capture(MQ, allowDelays: false);
                foreach (var task in snapshot)
                {
                    if (task.TimerSeconds >= TimerDisplayThresholdSeconds)
                    {
                        task.TimerSeconds = 0;
                        task.TimerDisplay = string.Empty;
                    }
                }

                _cachedTasks.Clear();
                _cachedTasks.AddRange(snapshot
                    .OrderBy(t => t.IsComplete)
                    .ThenBy(t => t.Type, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase));

                _lastDataUpdate = Core.StopWatch.ElapsedMilliseconds;

                RefreshPeerTaskData();
            }
            catch (ThreadAbort)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Write($"Failed to refresh task data: {ex.Message}", Logging.LogLevels.Error);
            }
        }

        private static void RefreshPeerTaskData()
        {
            _peerTasks.Clear();

            var sharedClient = NetMQServer.SharedDataClient;
            if (sharedClient == null) return;

            foreach (var kvp in sharedClient.TopicUpdates)
            {
                var bot = kvp.Key;
                if (string.IsNullOrWhiteSpace(bot)) continue;
                if (string.Equals(bot, E3.CurrentName, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(bot, "proxy", StringComparison.OrdinalIgnoreCase)) continue;

                var topics = kvp.Value;
                if (topics == null) continue;

                if (!topics.TryGetValue("E3Tasks", out var entry))
                {
                    continue;
                }

                var summaries = TaskDataCollector.DeserializeFromWire(entry.Data);

                _peerTasks.Add(new PeerTaskSummary
                {
                    Name = bot,
                    LastUpdate = entry.LastUpdate,
                    Tasks = summaries ?? new List<TaskWireSummary>()
                });
            }

            _peerTasks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static List<PeerTaskGroup> BuildPeerTaskGroups()
        {
            var groups = new Dictionary<string, PeerTaskGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var peer in _peerTasks)
            {
                if (peer.Tasks == null) continue;

                foreach (var task in peer.Tasks)
                {
                    string title = string.IsNullOrWhiteSpace(task.Title) ? "(Untitled Task)" : task.Title;
                    string type = task.Type ?? string.Empty;
                    string key = $"{title}|{type}";

                    if (!groups.TryGetValue(key, out var group))
                    {
                        group = new PeerTaskGroup
                        {
                            Title = title,
                            Type = type,
                            Key = key
                        };
                        groups[key] = group;
                    }

                    group.Entries.Add(new PeerTaskEntry
                    {
                        PeerName = peer.Name,
                        Summary = task
                    });
                }
            }

            return groups.Values
                .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void RenderGroupedTaskTable(PeerTaskGroup group)
        {
            using (var table = ImGUITable.Aquire())
            {
                int flags = (int)(ImGuiTableFlags.ImGuiTableFlags_RowBg |
                                   ImGuiTableFlags.ImGuiTableFlags_BordersInner |
                                   ImGuiTableFlags.ImGuiTableFlags_BordersOuter |
                                   ImGuiTableFlags.ImGuiTableFlags_Resizable);

                if (!table.BeginTable($"GroupedTask##{group.Title}", 3, flags, 0f, 0f))
                {
                    return;
                }

                imgui_TableSetupColumn("Peer", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthFixed, 140f);
                imgui_TableSetupColumn("Step", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthStretch, 260f);
                imgui_TableSetupColumn("Progress", (int)ImGuiTableColumnFlags.ImGuiTableColumnFlags_WidthFixed, 140f);
                imgui_TableHeadersRow();

                foreach (var entry in group.Entries
                    .OrderBy(e => e.PeerName, StringComparer.OrdinalIgnoreCase))
                {
                    imgui_TableNextRow();
                    imgui_TableNextColumn();
                    imgui_Text(entry.PeerName);

                    imgui_TableNextColumn();
                    string step = string.IsNullOrEmpty(entry.Summary.ActiveStep)
                        ? (entry.Summary.IsComplete ? "Complete" : "No step info yet")
                        : entry.Summary.ActiveStep;
                    imgui_TextWrapped(step);

                    imgui_TableNextColumn();
                    string progress;
                    if (entry.Summary.TotalObjectives > 0)
                    {
                        int completed = Math.Max(0, Math.Min(entry.Summary.CompletedObjectives, entry.Summary.TotalObjectives));
                        progress = $"{completed}/{entry.Summary.TotalObjectives}";
                    }
                    else
                    {
                        progress = entry.Summary.IsComplete ? "Done" : "0/0";
                    }

                    if (!string.IsNullOrEmpty(entry.Summary.TimerDisplay))
                    {
                        progress += $" ({entry.Summary.TimerDisplay})";
                    }

                    var color = entry.Summary.IsComplete ? CompletedColor : ActiveColor;
                    imgui_TextColored(color.R, color.G, color.B, 1f, progress);
                }
            }
        }

        private sealed class PeerTaskSummary
        {
            public string Name { get; set; } = string.Empty;
            public long LastUpdate { get; set; }
            public List<TaskWireSummary> Tasks { get; set; } = new List<TaskWireSummary>();
        }

        private sealed class PeerTaskGroup
        {
            public string Title { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public List<PeerTaskEntry> Entries { get; } = new List<PeerTaskEntry>();
        }

        private sealed class PeerTaskEntry
        {
            public string PeerName { get; set; } = string.Empty;
            public TaskWireSummary Summary { get; set; }
        }
    }
}
