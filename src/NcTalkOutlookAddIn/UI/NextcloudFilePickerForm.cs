// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NcTalkOutlookAddIn.Models;
using NcTalkOutlookAddIn.Services;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.UI
{
    internal enum NextcloudFilePickerMode
    {
        Files,
        Folder
    }

    internal sealed class NextcloudFilePickerForm : ScaledForm
    {
        private readonly FileLinkService _service;
        private readonly NextcloudFilePickerMode _mode;
        private readonly UiThemePalette _palette =
            UiThemeManager.DetectPalette();
        private readonly TextBox _filterTextBox = new TextBox();
        private readonly Button _upButton = new Button();
        private readonly PictureBox _sourceIcon = new PictureBox();
        private readonly Label _accountLabel = new Label();
        private readonly Label _pathLabel = new Label();
        private readonly ListView _itemsView = new ListView();
        private readonly Label _previewLabel = new Label();
        private readonly Label _storageLabel = new Label();
        private readonly Label _statusLabel = new Label();
        private readonly Button _closeButton = new Button();
        private readonly Button _selectButton = new Button();
        private readonly ImageList _icons;
        private CancellationTokenSource _loadCancellation;
        private string _currentPath = string.Empty;
        private NextcloudStorageListing _currentListing;
        private long? _usedBytes;
        private long? _availableBytes;
        private bool _loading;

        internal NextcloudFilePickerForm(
            FileLinkService service,
            NextcloudFilePickerMode mode)
        {
            if (service == null)
            {
                throw new ArgumentNullException("service");
            }

            _service = service;
            _mode = mode;
            _icons = FileLinkIconProvider.CreateImageList(
                ScaleLogical(24),
                ScaleLogical(28));
            SelectedEntries = new ReadOnlyCollection<NextcloudStorageEntry>(
                new List<NextcloudStorageEntry>());

            Text = mode == NextcloudFilePickerMode.Files
                ? Strings.NextcloudPickerFilesTitle
                : Strings.NextcloudPickerFolderTitle;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(
                ScaleLogical(860),
                ScaleLogical(640));
            MinimumSize = new Size(
                ScaleLogical(720),
                ScaleLogical(520));
            Icon = BrandingAssets.GetAppIcon(32);

            InitializeLayout();
            UiThemeManager.ApplyToForm(this);
            _filterTextBox.ForeColor = _palette.MutedText;
            Shown += async (s, e) =>
                await LoadFolderAsync(string.Empty);
        }

        internal ReadOnlyCollection<NextcloudStorageEntry> SelectedEntries
        {
            get;
            private set;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CancelCurrentLoad();
            if (_sourceIcon.Image != null)
            {
                _sourceIcon.Image.Dispose();
                _sourceIcon.Image = null;
            }
            _icons.Dispose();
            base.OnFormClosed(e);
        }

        private void InitializeLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(52)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(44)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(56)));
            Controls.Add(root);

            var searchPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(
                    ScaleLogical(12),
                    ScaleLogical(12),
                    ScaleLogical(12),
                    ScaleLogical(8))
            };
            root.Controls.Add(searchPanel, 0, 0);

            _filterTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _filterTextBox.Width = ScaleLogical(190);
            _filterTextBox.Location = new Point(
                Math.Max(
                    0,
                    searchPanel.ClientSize.Width
                    - _filterTextBox.Width
                    - ScaleLogical(12)),
                ScaleLogical(12));
            _filterTextBox.Text = Strings.NextcloudPickerFilterPlaceholder;
            _filterTextBox.ForeColor = _palette.MutedText;
            _filterTextBox.GotFocus += HandleFilterGotFocus;
            _filterTextBox.LostFocus += HandleFilterLostFocus;
            _filterTextBox.TextChanged += HandleFilterChanged;
            searchPanel.Controls.Add(_filterTextBox);
            searchPanel.Resize += (s, e) =>
            {
                _filterTextBox.Left = Math.Max(
                    ScaleLogical(12),
                    searchPanel.ClientSize.Width
                    - _filterTextBox.Width
                    - ScaleLogical(12));
            };

            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(
                    ScaleLogical(8),
                    ScaleLogical(5),
                    ScaleLogical(12),
                    ScaleLogical(5)),
                Margin = new Padding(0)
            };
            pathPanel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, ScaleLogical(32)));
            pathPanel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, ScaleLogical(30)));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathPanel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100f));
            root.Controls.Add(pathPanel, 0, 1);

            _upButton.Text = "‹";
            _upButton.Dock = DockStyle.Fill;
            _upButton.Margin = new Padding(0);
            _upButton.Enabled = false;
            _upButton.Click += async (s, e) => await NavigateUpAsync();
            pathPanel.Controls.Add(_upButton, 0, 0);

            _sourceIcon.Dock = DockStyle.Fill;
            _sourceIcon.SizeMode = PictureBoxSizeMode.CenterImage;
            _sourceIcon.Margin = new Padding(0);
            _sourceIcon.Image = new Bitmap(
                _icons.Images[FileLinkIconProvider.NextcloudSourceKey]);
            pathPanel.Controls.Add(_sourceIcon, 1, 0);

            _accountLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                Strings.NextcloudPickerAccountFormat,
                _service.NextcloudAccountName);
            _accountLabel.AutoSize = true;
            _accountLabel.Anchor = AnchorStyles.Left;
            _accountLabel.Margin = new Padding(0, 0, ScaleLogical(10), 0);
            pathPanel.Controls.Add(_accountLabel, 2, 0);

            _pathLabel.Text = "/";
            _pathLabel.AutoEllipsis = true;
            _pathLabel.Dock = DockStyle.Fill;
            _pathLabel.TextAlign = ContentAlignment.MiddleLeft;
            pathPanel.Controls.Add(_pathLabel, 3, 0);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Size = new Size(
                    ScaleLogical(860),
                    ScaleLogical(480)),
                Orientation = Orientation.Vertical,
                SplitterWidth = ScaleLogical(1),
                SplitterDistance = ScaleLogical(560),
                Panel1MinSize = ScaleLogical(390),
                Panel2MinSize = ScaleLogical(180),
                Margin = new Padding(0)
            };
            root.Controls.Add(split, 0, 2);

            _itemsView.Dock = DockStyle.Fill;
            _itemsView.BorderStyle = BorderStyle.FixedSingle;
            _itemsView.View = View.Details;
            _itemsView.FullRowSelect = true;
            _itemsView.HideSelection = false;
            _itemsView.MultiSelect = _mode == NextcloudFilePickerMode.Files;
            _itemsView.SmallImageList = _icons;
            _itemsView.Columns.Add(
                Strings.NextcloudPickerColumnName,
                ScaleLogical(300));
            _itemsView.Columns.Add(
                Strings.NextcloudPickerColumnSize,
                ScaleLogical(95));
            _itemsView.Columns.Add(
                Strings.NextcloudPickerColumnModified,
                ScaleLogical(130));
            _itemsView.SelectedIndexChanged += (s, e) =>
            {
                UpdatePreview();
                UpdateSelectButton();
            };
            _itemsView.ItemActivate += async (s, e) =>
                await ActivateSelectedItemAsync();
            _itemsView.KeyDown += async (s, e) =>
                await HandleItemsKeyDownAsync(e);
            _itemsView.Resize += (s, e) => UpdateColumnWidths();
            split.Panel1.Controls.Add(_itemsView);

            _previewLabel.Dock = DockStyle.Fill;
            _previewLabel.Text = Strings.NextcloudPickerNoSelection;
            _previewLabel.TextAlign = ContentAlignment.MiddleCenter;
            _previewLabel.ForeColor = _palette.MutedText;
            _previewLabel.Padding = new Padding(ScaleLogical(16));
            split.Panel2.Controls.Add(_previewLabel);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(
                    ScaleLogical(12),
                    ScaleLogical(10),
                    ScaleLogical(12),
                    ScaleLogical(10)),
                Margin = new Padding(0)
            };
            footer.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(footer, 0, 3);

            _storageLabel.AutoSize = true;
            _storageLabel.Anchor = AnchorStyles.Left;
            footer.Controls.Add(_storageLabel, 0, 0);

            _statusLabel.AutoSize = true;
            _statusLabel.Anchor = AnchorStyles.Right;
            _statusLabel.ForeColor = _palette.MutedText;
            _statusLabel.Margin = new Padding(0, 0, ScaleLogical(12), 0);
            footer.Controls.Add(_statusLabel, 1, 0);

            _closeButton.Text = Strings.NextcloudPickerCloseButton;
            _closeButton.DialogResult = DialogResult.Cancel;
            _closeButton.AutoSize = true;
            _closeButton.Margin = new Padding(0, 0, ScaleLogical(8), 0);
            footer.Controls.Add(_closeButton, 2, 0);

            _selectButton.Text = _mode == NextcloudFilePickerMode.Files
                ? Strings.NextcloudPickerSelectFilesButton
                : Strings.NextcloudPickerSelectFolderButton;
            _selectButton.AutoSize = true;
            _selectButton.Enabled = false;
            _selectButton.Click += async (s, e) => await ConfirmAsync();
            footer.Controls.Add(_selectButton, 3, 0);

            CancelButton = _closeButton;
            AcceptButton = _selectButton;
            UpdateColumnWidths();
        }

        private async Task LoadFolderAsync(string relativePath)
        {
            CancelCurrentLoad();
            _loadCancellation = new CancellationTokenSource();
            CancellationToken token = _loadCancellation.Token;
            SetLoading(true, Strings.NextcloudPickerLoading);
            try
            {
                NextcloudStorageListing listing = await Task.Run(
                    () => _service.ListNextcloudDirectory(
                        relativePath,
                        token),
                    token);
                token.ThrowIfCancellationRequested();
                _currentPath = listing.RelativePath;
                _currentListing = listing;
                if (listing.UsedBytes.HasValue)
                {
                    _usedBytes = listing.UsedBytes;
                }
                if (listing.AvailableBytes.HasValue)
                {
                    _availableBytes = listing.AvailableBytes;
                }
                _pathLabel.Text = "/" + _currentPath;
                _upButton.Enabled = _currentPath.Length > 0;
                PopulateItems();
                UpdateStorageLabel();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.LogException(
                    LogCategories.FileLink,
                    "Nextcloud picker folder load failed.",
                    ex);
                MessageBox.Show(
                    ex.Message,
                    Strings.DialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    SetLoading(false, string.Empty);
                }
            }
        }

        private void PopulateItems()
        {
            string filter = GetActiveFilter();
            IEnumerable<NextcloudStorageEntry> entries =
                _currentListing != null
                    ? _currentListing.Entries
                    : Enumerable.Empty<NextcloudStorageEntry>();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                entries = entries.Where(
                    entry => entry.DisplayName.IndexOf(
                        filter,
                        StringComparison.CurrentCultureIgnoreCase) >= 0);
            }

            _itemsView.BeginUpdate();
            try
            {
                _itemsView.Items.Clear();
                foreach (NextcloudStorageEntry entry in entries)
                {
                    string imageKey = entry.IsDirectory
                        ? FileLinkIconProvider.FolderKey
                        : FileLinkIconProvider.GetFileKey(
                            entry.DisplayName);
                    var item = new ListViewItem(
                        entry.DisplayName,
                        imageKey)
                    {
                        Tag = entry
                    };
                    item.SubItems.Add(
                        entry.IsDirectory
                            ? string.Empty
                            : SizeFormatting.FormatBytes(entry.Length));
                    item.SubItems.Add(
                        entry.LastModifiedUtc.HasValue
                            ? entry.LastModifiedUtc.Value
                                .ToLocalTime()
                                .ToString(
                                    "d",
                                    CultureInfo.CurrentCulture)
                            : string.Empty);
                    _itemsView.Items.Add(item);
                }
            }
            finally
            {
                _itemsView.EndUpdate();
            }
            UpdatePreview();
            UpdateSelectButton();
        }

        private async Task NavigateUpAsync()
        {
            if (_loading || _currentPath.Length == 0)
            {
                return;
            }
            await LoadFolderAsync(NextcloudPath.GetParent(_currentPath));
        }

        private async Task ActivateSelectedItemAsync()
        {
            if (_loading || _itemsView.SelectedItems.Count != 1)
            {
                return;
            }
            NextcloudStorageEntry entry =
                _itemsView.SelectedItems[0].Tag
                as NextcloudStorageEntry;
            if (entry == null)
            {
                return;
            }
            if (entry.IsDirectory)
            {
                await LoadFolderAsync(entry.RelativePath);
            }
            else if (_mode == NextcloudFilePickerMode.Files)
            {
                await ConfirmAsync();
            }
        }

        private async Task ConfirmAsync()
        {
            if (_loading)
            {
                return;
            }
            if (_mode == NextcloudFilePickerMode.Files)
            {
                List<NextcloudStorageEntry> files = _itemsView
                    .SelectedItems
                    .Cast<ListViewItem>()
                    .Select(item => item.Tag as NextcloudStorageEntry)
                    .Where(entry => entry != null && !entry.IsDirectory)
                    .ToList();
                if (files.Count == 0)
                {
                    return;
                }
                SelectedEntries = new ReadOnlyCollection<NextcloudStorageEntry>(
                    files);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            string folderName = _currentPath.Length == 0
                ? Strings.FileLinkSourceMyNextcloud
                : ResolvePathName(_currentPath);
            var root = new NextcloudStorageEntry(
                _currentPath,
                folderName,
                true,
                0,
                null);
            SetLoading(true, Strings.NextcloudPickerReadingFolder);
            try
            {
                CancellationToken token = _loadCancellation != null
                    ? _loadCancellation.Token
                    : CancellationToken.None;
                IList<NextcloudStorageEntry> snapshot = await Task.Run(
                    () => _service.SnapshotNextcloudFolder(
                        root,
                        null,
                        token),
                    token);
                token.ThrowIfCancellationRequested();
                var result = new List<NextcloudStorageEntry> { root };
                result.AddRange(snapshot);
                SelectedEntries =
                    new ReadOnlyCollection<NextcloudStorageEntry>(result);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.LogException(
                    LogCategories.FileLink,
                    "Nextcloud picker folder snapshot failed.",
                    ex);
                MessageBox.Show(
                    ex.Message,
                    Strings.DialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetLoading(false, string.Empty);
                }
            }
        }

        private async Task HandleItemsKeyDownAsync(
            KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await NavigateUpAsync();
            }
        }

        private void HandleFilterGotFocus(object sender, EventArgs e)
        {
            if (string.Equals(
                _filterTextBox.Text,
                Strings.NextcloudPickerFilterPlaceholder,
                StringComparison.Ordinal))
            {
                _filterTextBox.Text = string.Empty;
                _filterTextBox.ForeColor = _palette.Text;
            }
        }

        private void HandleFilterLostFocus(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_filterTextBox.Text))
            {
                _filterTextBox.Text =
                    Strings.NextcloudPickerFilterPlaceholder;
                _filterTextBox.ForeColor = _palette.MutedText;
            }
        }

        private void HandleFilterChanged(object sender, EventArgs e)
        {
            if (_currentListing != null)
            {
                PopulateItems();
            }
        }

        private string GetActiveFilter()
        {
            string value = _filterTextBox.Text ?? string.Empty;
            return string.Equals(
                       value,
                       Strings.NextcloudPickerFilterPlaceholder,
                       StringComparison.Ordinal)
                ? string.Empty
                : value.Trim();
        }

        private void UpdatePreview()
        {
            if (_itemsView.SelectedItems.Count != 1)
            {
                _previewLabel.Text = Strings.NextcloudPickerNoSelection;
                return;
            }
            NextcloudStorageEntry entry =
                _itemsView.SelectedItems[0].Tag
                as NextcloudStorageEntry;
            _previewLabel.Text = entry == null
                ? Strings.NextcloudPickerNoSelection
                : entry.IsDirectory
                    ? entry.DisplayName
                    : Strings.NextcloudPickerNoPreview;
        }

        private void UpdateSelectButton()
        {
            if (_loading)
            {
                _selectButton.Enabled = false;
                return;
            }
            _selectButton.Enabled =
                _mode == NextcloudFilePickerMode.Folder
                || _itemsView.SelectedItems
                    .Cast<ListViewItem>()
                    .Select(item => item.Tag as NextcloudStorageEntry)
                    .Any(entry => entry != null && !entry.IsDirectory);
        }

        private void UpdateStorageLabel()
        {
            if (!_usedBytes.HasValue)
            {
                _storageLabel.Text = string.Empty;
                return;
            }
            if (_availableBytes.HasValue)
            {
                long total = checked(
                    _usedBytes.Value + _availableBytes.Value);
                _storageLabel.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.NextcloudPickerStorageOfFormat,
                    SizeFormatting.FormatBytes(_usedBytes.Value),
                    SizeFormatting.FormatBytes(total));
                return;
            }
            _storageLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                Strings.NextcloudPickerStorageUsedFormat,
                SizeFormatting.FormatBytes(_usedBytes.Value));
        }

        private void SetLoading(bool loading, string status)
        {
            _loading = loading;
            _statusLabel.Text = status ?? string.Empty;
            _itemsView.Enabled = !loading;
            _filterTextBox.Enabled = !loading;
            _upButton.Enabled = !loading && _currentPath.Length > 0;
            UseWaitCursor = loading;
            UpdateSelectButton();
        }

        private void CancelCurrentLoad()
        {
            if (_loadCancellation == null)
            {
                return;
            }
            try
            {
                _loadCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            _loadCancellation.Dispose();
            _loadCancellation = null;
        }

        private void UpdateColumnWidths()
        {
            if (_itemsView.Columns.Count < 3)
            {
                return;
            }
            int available = Math.Max(
                ScaleLogical(360),
                _itemsView.ClientSize.Width - ScaleLogical(6));
            int sizeWidth = ScaleLogical(96);
            int modifiedWidth = ScaleLogical(132);
            _itemsView.Columns[0].Width = Math.Max(
                ScaleLogical(160),
                available - sizeWidth - modifiedWidth);
            _itemsView.Columns[1].Width = sizeWidth;
            _itemsView.Columns[2].Width = modifiedWidth;
        }

        private static string ResolvePathName(string path)
        {
            return NextcloudPath.GetName(path);
        }
    }
}
