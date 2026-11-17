using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public enum RepositorySidebarRowKind
    {
        Group,
        Repository,
        Submodule,
    }

    public class RepositorySidebarRow : ObservableObject
    {
        public RepositorySidebarRowKind Kind { get; init; }
        public RepositoryNode Node { get; init; }
        public string Path { get; init; }
        public string Name { get; init; }
        public string OwnerName { get; init; }
        public int Depth { get; init; }
        public bool CanExpand { get; init; }
        public bool IsExpanded { get; init; }
        public bool IsSubmoduleDirty { get; init; }

        public bool IsGroup => Kind == RepositorySidebarRowKind.Group;
        public bool IsSubmodule => Kind == RepositorySidebarRowKind.Submodule;
        public bool IsRepository => Kind == RepositorySidebarRowKind.Repository;
    }

    public class RepositorySidebar : ObservableObject
    {
        public const int MaxOpenRepositories = 8;

        public static RepositorySidebar Instance { get; private set; }

        public AvaloniaList<RepositorySidebarRow> Rows { get; } = [];

        public RepositorySidebarRow SelectedRow
        {
            get => _selectedRow;
            set => SetProperty(ref _selectedRow, value);
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                    Refresh();
            }
        }

        public RepositorySidebar(Launcher launcher)
        {
            Instance = this;
            _launcher = launcher;
            _launcher.PropertyChanged += OnLauncherPropertyChanged;
            Refresh();
            _ = Welcome.Instance.UpdateStatusAsync(false, null);
        }

        public void Refresh()
        {
            var rows = new List<RepositorySidebarRow>();
            if (string.IsNullOrWhiteSpace(_searchFilter))
                MakeTreeRows(rows, Preferences.Instance.RepositoryNodes, 0);
            else
                MakeSearchRows(rows, Preferences.Instance.RepositoryNodes);

            Rows.Clear();
            Rows.AddRange(rows);
            SyncSelection();
        }

        public async void Toggle(RepositorySidebarRow row)
        {
            if (row.IsGroup)
            {
                row.Node.IsExpanded = !row.Node.IsExpanded;
                Welcome.Instance.Refresh();
                return;
            }

            if (!row.CanExpand)
                return;

            if (!_expanded.Remove(row.Path))
            {
                _expanded.Add(row.Path);
                if (!_submodules.ContainsKey(row.Path))
                    _submodules[row.Path] = await new Commands.QuerySubmodules(row.Path).GetResultAsync();
            }

            Refresh();
        }

        public void Open(RepositorySidebarRow row)
        {
            switch (row.Kind)
            {
                case RepositorySidebarRowKind.Group:
                    Toggle(row);
                    return;
                case RepositorySidebarRowKind.Repository:
                    _launcher.OpenRepositoryInTab(row.Node, null);
                    break;
                case RepositorySidebarRowKind.Submodule:
                    var node = Preferences.Instance.FindNode(row.Path) ?? new RepositoryNode
                    {
                        Id = row.Path,
                        Name = $"{row.OwnerName}: {row.Name}",
                        IsRepository = true,
                        IsUnmanaged = true,
                    };
                    _launcher.OpenRepositoryInTab(node, null);
                    break;
            }

            CloseLeastRecentlyUsedTabs();
        }

        private void OnLauncherPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Launcher.ActivePage))
                return;

            if (_launcher.ActivePage is { } page)
            {
                _recentPages.Remove(page);
                _recentPages.Add(page);

                if (page.Data is Repository)
                    _ = page.Node.UpdateStatusAsync(true, null);
            }

            SyncSelection();
        }

        private void SyncSelection()
        {
            var id = _launcher.ActivePage?.Node.Id;
            SelectedRow = Rows.FirstOrDefault(r => !r.IsGroup && r.Path == id);
        }

        private void CloseLeastRecentlyUsedTabs()
        {
            _recentPages.RemoveAll(p => !_launcher.Pages.Contains(p));

            var repoPages = _launcher.Pages.Where(p => p.Data is Repository).ToList();
            var excess = repoPages.Count - MaxOpenRepositories;
            if (excess <= 0)
                return;

            var candidates = repoPages
                .Where(p => p != _launcher.ActivePage && p.Popup == null)
                .OrderBy(p => _recentPages.IndexOf(p))
                .Take(excess)
                .ToList();

            foreach (var page in candidates)
            {
                _launcher.CloseTab(page);
                _recentPages.Remove(page);
            }
        }

        private void MakeTreeRows(List<RepositorySidebarRow> rows, List<RepositoryNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                if (!node.IsRepository)
                {
                    rows.Add(new RepositorySidebarRow
                    {
                        Kind = RepositorySidebarRowKind.Group,
                        Node = node,
                        Path = node.Id,
                        Name = node.Name,
                        Depth = depth,
                        CanExpand = true,
                        IsExpanded = node.IsExpanded,
                    });

                    if (node.IsExpanded)
                        MakeTreeRows(rows, node.SubNodes, depth + 1);

                    continue;
                }

                AddRepositoryRow(rows, node, node.Id, node.Name, null, depth, RepositorySidebarRowKind.Repository, false);
            }
        }

        private void MakeSearchRows(List<RepositorySidebarRow> rows, List<RepositoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsRepository)
                {
                    MakeSearchRows(rows, node.SubNodes);
                    continue;
                }

                if (Matches(node.Name) || Matches(node.Id))
                    AddRepositoryRow(rows, node, node.Id, node.Name, null, 0, RepositorySidebarRowKind.Repository, false);

                AddMatchedSubmodules(rows, node.Id, node.Name);
            }
        }

        private void AddMatchedSubmodules(List<RepositorySidebarRow> rows, string ownerPath, string ownerName)
        {
            if (!_submodules.TryGetValue(ownerPath, out var submodules))
                return;

            foreach (var sub in submodules)
            {
                var path = $"{ownerPath}/{sub.Path}";
                if (Matches(sub.Path))
                    rows.Add(MakeSubmoduleRow(path, sub, ownerName));

                AddMatchedSubmodules(rows, path, sub.Path);
            }
        }

        private void AddRepositoryRow(List<RepositorySidebarRow> rows, RepositoryNode node, string path, string name, string ownerName, int depth, RepositorySidebarRowKind kind, bool dirty)
        {
            var canExpand = File.Exists(System.IO.Path.Combine(path, ".gitmodules"));
            var isExpanded = canExpand && _expanded.Contains(path);

            rows.Add(new RepositorySidebarRow
            {
                Kind = kind,
                Node = node,
                Path = path,
                Name = name,
                OwnerName = ownerName,
                Depth = depth,
                CanExpand = canExpand,
                IsExpanded = isExpanded,
                IsSubmoduleDirty = dirty,
            });

            if (!isExpanded || !_submodules.TryGetValue(path, out var submodules))
                return;

            foreach (var sub in submodules)
                AddRepositoryRow(rows, null, $"{path}/{sub.Path}", sub.Path, name, depth + 1, RepositorySidebarRowKind.Submodule, sub.IsDirty);
        }

        private static RepositorySidebarRow MakeSubmoduleRow(string path, Models.Submodule sub, string ownerName) => new()
        {
            Kind = RepositorySidebarRowKind.Submodule,
            Path = path,
            Name = sub.Path,
            OwnerName = ownerName,
            IsSubmoduleDirty = sub.IsDirty,
        };

        private bool Matches(string text) => text.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);

        private readonly Launcher _launcher;
        private readonly HashSet<string> _expanded = [];
        private readonly Dictionary<string, List<Models.Submodule>> _submodules = [];
        private readonly List<LauncherPage> _recentPages = [];
        private RepositorySidebarRow _selectedRow;
        private string _searchFilter = string.Empty;
    }
}
