using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class RepositorySidebar : UserControl
    {
        public RepositorySidebar()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (RowsList.SelectedItem is { } selected)
                RowsList.ScrollIntoView(selected);
        }

        private ViewModels.RepositorySidebar VM => DataContext as ViewModels.RepositorySidebar;

        private void OnCreateGroup(object sender, RoutedEventArgs e)
        {
            ViewModels.Welcome.Instance.AddRootNode();
            e.Handled = true;
        }

        private void OnOpenLocalRepository(object sender, RoutedEventArgs e)
        {
            ViewModels.Welcome.Instance.OpenLocalRepository();
            e.Handled = true;
        }

        private void OnCloneRepository(object sender, RoutedEventArgs e)
        {
            ViewModels.Welcome.Instance.Clone();
            e.Handled = true;
        }

        private void OnClearSearchFilter(object sender, RoutedEventArgs e)
        {
            if (VM is { } vm)
                vm.SearchFilter = string.Empty;

            e.Handled = true;
        }

        private void OnRowTapped(object sender, TappedEventArgs e)
        {
            if (e.Source is Visual source && source.FindAncestorOfType<ToggleButton>(true) != null)
                return;

            if (sender is Control { DataContext: ViewModels.RepositorySidebarRow row })
                VM?.Open(row);

            e.Handled = true;
        }

        private void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: ViewModels.RepositorySidebarRow row })
                VM?.Toggle(row);

            e.Handled = true;
        }

        private void OnRowsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyModifiers != KeyModifiers.None ||
                RowsList.SelectedItem is not ViewModels.RepositorySidebarRow row ||
                VM is not { } vm)
                return;

            switch (e.Key)
            {
                case Key.Enter:
                    vm.Open(row);
                    e.Handled = true;
                    break;
                case Key.Right when row.CanExpand && !row.IsExpanded:
                case Key.Left when row.CanExpand && row.IsExpanded:
                    vm.Toggle(row);
                    e.Handled = true;
                    break;
            }
        }

        private void OnListContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Items.Add(MakeMenuItem("Welcome.AddRootFolder", "Icons.Folder.Add", ViewModels.Welcome.Instance.AddRootNode));
            menu.Open(RowsList);
            e.Handled = true;
        }

        private void OnRowContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (sender is not Control { DataContext: ViewModels.RepositorySidebarRow row } control)
                return;

            var menu = new ContextMenu();
            var node = row.Node;
            var exists = Directory.Exists(row.Path);

            switch (row.Kind)
            {
                case ViewModels.RepositorySidebarRowKind.Group:
                    menu.Items.Add(MakeMenuItem("Welcome.AddSubFolder", "Icons.Folder.Add", node.AddSubFolder));
                    menu.Items.Add(MakeMenuItem("Welcome.Edit", "Icons.Edit", node.Edit));
                    menu.Items.Add(MakeMenuItem("Welcome.Move", "Icons.MoveTo", node.Move));
                    menu.Items.Add(new MenuItem { Header = "-" });
                    menu.Items.Add(MakeMenuItem("Welcome.Delete", "Icons.Clear", node.Delete));
                    break;
                case ViewModels.RepositorySidebarRowKind.Repository:
                    if (exists)
                    {
                        menu.Items.Add(MakeMenuItem("Repository.Explore", "Icons.Explore", node.OpenInFileManager));
                        menu.Items.Add(MakeMenuItem("Repository.Terminal", "Icons.Terminal", node.OpenTerminal));
                        menu.Items.Add(new MenuItem { Header = "-" });
                    }
                    menu.Items.Add(MakeMenuItem("Welcome.Edit", "Icons.Edit", node.Edit));
                    menu.Items.Add(MakeMenuItem("Welcome.Move", "Icons.MoveTo", node.Move));
                    menu.Items.Add(new MenuItem { Header = "-" });
                    menu.Items.Add(MakeMenuItem("Welcome.Delete", "Icons.Clear", node.Delete));
                    break;
                case ViewModels.RepositorySidebarRowKind.Submodule when exists:
                    menu.Items.Add(MakeMenuItem("Repository.Explore", "Icons.Explore", () => Native.OS.OpenInFileManager(row.Path)));
                    menu.Items.Add(MakeMenuItem("Repository.Terminal", "Icons.Terminal", () => Native.OS.OpenTerminal(row.Path)));
                    break;
                default:
                    return;
            }

            menu.Open(control);
            e.Handled = true;
        }

        private MenuItem MakeMenuItem(string textKey, string iconKey, System.Action action)
        {
            var item = new MenuItem { Header = App.Text(textKey), Icon = this.CreateMenuIcon(iconKey) };
            item.Click += (_, ev) =>
            {
                action();
                ev.Handled = true;
            };
            return item;
        }

        private void OnRowPointerPressed(object sender, PointerPressedEventArgs e)
        {
            _pressEvent = e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed ? e : null;
            _isDragging = false;
        }

        private void OnRowPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            _pressEvent = null;
            _isDragging = false;
        }

        private async void OnRowPointerMoved(object sender, PointerEventArgs e)
        {
            if (_pressEvent == null || _isDragging ||
                sender is not Control { DataContext: ViewModels.RepositorySidebarRow { Node: { } node, IsSubmodule: false } } control)
                return;

            var delta = e.GetPosition(control) - _pressEvent.GetPosition(control);
            if (delta.X * delta.X + delta.Y * delta.Y < 64)
                return;

            _isDragging = true;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(_dndNodeFormat, node.Id));
            await DragDrop.DoDragDropAsync(_pressEvent, data, DragDropEffects.Move);
            _pressEvent = null;
            _isDragging = false;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(_dndNodeFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDropOnRow(object sender, DragEventArgs e)
        {
            if (sender is not Control { DataContext: ViewModels.RepositorySidebarRow row } ||
                e.DataTransfer.TryGetValue(_dndNodeFormat) is not { Length: > 0 } id)
                return;

            var welcome = ViewModels.Welcome.Instance;
            var target = row.Kind switch
            {
                ViewModels.RepositorySidebarRowKind.Group => row.Node,
                ViewModels.RepositorySidebarRowKind.Repository => welcome.FindParentGroup(row.Node),
                _ => null,
            };

            var moved = welcome.FindNodeById(id);
            if (moved != null && moved != target)
                welcome.MoveNode(moved, target);

            e.Handled = true;
        }

        private void OnDropOnList(object sender, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetValue(_dndNodeFormat) is { Length: > 0 } id &&
                ViewModels.Welcome.Instance.FindNodeById(id) is { } moved)
                ViewModels.Welcome.Instance.MoveNode(moved, null);

            e.Handled = true;
        }

        private PointerPressedEventArgs _pressEvent;
        private bool _isDragging;
        private readonly DataFormat<string> _dndNodeFormat = DataFormat.CreateStringApplicationFormat("sourcegit-sidebar-repo-node");
    }
}
