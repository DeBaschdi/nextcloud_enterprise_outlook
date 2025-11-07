/**
 * Copyright (c) 2025 Bastian Kleinschmidt
 * Licensed under the GNU Affero General Public License v3.0.
 * See LICENSE.txt for details.
 */

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using NcTalkOutlookAddIn.Models;
using NcTalkOutlookAddIn.Settings;
using NcTalkOutlookAddIn.Utilities;

namespace NcTalkOutlookAddIn.UI
{

    /// <summary>
    /// Dialog zur Konfiguration und Erstellung eines Nextcloud Talk Raums fuer den aktiven Termin.
    /// </summary>
    internal sealed class TalkLinkForm : Form
    {
        private static readonly string DefaultTitle = Strings.TalkDefaultTitle;
        private const int MinPasswordLength = 5;

        private readonly TextBox _titleTextBox = new TextBox();
        private readonly TextBox _passwordTextBox = new TextBox();
        private readonly CheckBox _lobbyCheckBox = new CheckBox();
        private readonly CheckBox _searchCheckBox = new CheckBox();
        private readonly RadioButton _eventConversationRadio = new RadioButton();
        private readonly RadioButton _standardRoomRadio = new RadioButton();
        private readonly Button _okButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly Panel _headerPanel = new Panel();
        private readonly PictureBox _headerLogo = new PictureBox();
        private const int HeaderHeight = 34;
        private readonly bool _eventConversationsSupported;
        private readonly string _serverVersionHint;

        private string _talkTitle;
        private string _talkPassword;
        private bool _lobbyUntilStart;
        private bool _searchVisible;
        private TalkRoomType _selectedRoomType;

        internal string TalkTitle
        {
            get { return _talkTitle; }
            private set { _talkTitle = value; }
        }

        internal string TalkPassword
        {
            get { return _talkPassword; }
            private set { _talkPassword = value; }
        }

        internal bool LobbyUntilStart
        {
            get { return _lobbyUntilStart; }
            private set { _lobbyUntilStart = value; }
        }

        internal bool SearchVisible
        {
            get { return _searchVisible; }
            private set { _searchVisible = value; }
        }

        internal TalkRoomType SelectedRoomType
        {
            get { return _selectedRoomType; }
            private set { _selectedRoomType = value; }
        }

        internal TalkLinkForm(AddinSettings defaults, string appointmentSubject, DateTime startTime, DateTime endTime)
        {
            _eventConversationsSupported = DetermineEventConversationSupport(defaults, out _serverVersionHint);

            Text = Strings.TalkFormTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 320);

            InitializeHeader();
            InitializeComponents();
            ApplyDefaults(appointmentSubject);
        }

        /**
         * Baut alle Dialog-Steuerelemente (Titel, Passwort, Optionen, Buttons) auf.
         */
        private void InitializeComponents()
        {
            int topOffset = _headerPanel.Bottom + 15;

            var titleLabel = new Label
            {
                Text = Strings.TalkTitleLabel,
                Location = new Point(15, topOffset),
                AutoSize = true
            };

            _titleTextBox.Location = new Point(150, topOffset - 4);
            _titleTextBox.Width = 260;

            var passwordLabel = new Label
            {
                Text = Strings.TalkPasswordLabel,
                Location = new Point(15, topOffset + 40),
                AutoSize = true
            };

            _passwordTextBox.Location = new Point(150, topOffset + 36);
            _passwordTextBox.Width = 260;
            _passwordTextBox.UseSystemPasswordChar = true;

            _lobbyCheckBox.Text = Strings.TalkLobbyCheck;
            _lobbyCheckBox.Location = new Point(18, topOffset + 80);
            _lobbyCheckBox.AutoSize = true;

            _searchCheckBox.Text = Strings.TalkSearchCheck;
            _searchCheckBox.Location = new Point(18, topOffset + 110);
            _searchCheckBox.AutoSize = true;

            var roomTypeGroup = new GroupBox
            {
                Text = Strings.TalkRoomGroup,
                Location = new Point(18, topOffset + 150),
                Size = new Size(392, _eventConversationsSupported ? 70 : 100)
            };

            _eventConversationRadio.Text = Strings.TalkEventRadio;
            _eventConversationRadio.Location = new Point(12, 22);
            _eventConversationRadio.AutoSize = true;
            _eventConversationRadio.Enabled = _eventConversationsSupported;

            _standardRoomRadio.Text = Strings.TalkStandardRadio;
            _standardRoomRadio.Location = new Point(12, 42);
            _standardRoomRadio.AutoSize = true;

            roomTypeGroup.Controls.Add(_eventConversationRadio);
            roomTypeGroup.Controls.Add(_standardRoomRadio);

            if (!_eventConversationsSupported)
            {
                var versionInfo = string.IsNullOrEmpty(_serverVersionHint) ? Strings.TalkVersionUnknown : _serverVersionHint;
                var hintLabel = new Label
                {
                    Text = string.Format(Strings.TalkEventHint, versionInfo),
                    Location = new Point(12, 64),
                    Size = new Size(360, 32),
                    ForeColor = Color.DimGray
                };
                roomTypeGroup.Controls.Add(hintLabel);
            }

            _okButton.Text = Strings.DialogOk;
            _okButton.Location = new Point(230, 280);
            _okButton.Width = 90;
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Click += OnOkButtonClick;

            _cancelButton.Text = Strings.DialogCancel;
            _cancelButton.Location = new Point(325, 280);
            _cancelButton.Width = 90;
            _cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(titleLabel);
            Controls.Add(_titleTextBox);
            Controls.Add(passwordLabel);
            Controls.Add(_passwordTextBox);
            Controls.Add(_lobbyCheckBox);
            Controls.Add(_searchCheckBox);
            Controls.Add(roomTypeGroup);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        private void InitializeHeader()
        {
            _headerPanel.Height = HeaderHeight;
            _headerPanel.Dock = DockStyle.Top;
            _headerPanel.BackColor = Color.FromArgb(0, 120, 212);

            _headerLogo.Size = new Size(24, 24);
            _headerLogo.SizeMode = PictureBoxSizeMode.Zoom;
            _headerLogo.Image = LoadEmbeddedImage("NcTalkOutlookAddIn.Resources.talk-96.png");
            _headerPanel.Controls.Add(_headerLogo);
            _headerPanel.Resize += (s, e) => CenterHeaderLogo();

            Controls.Add(_headerPanel);
            CenterHeaderLogo();
        }

        private void CenterHeaderLogo()
        {
            if (_headerPanel == null || _headerLogo == null)
            {
                return;
            }

            int x = (_headerPanel.Width - _headerLogo.Width) / 2;
            int y = (_headerPanel.Height - _headerLogo.Height) / 2;
            _headerLogo.Location = new Point(Math.Max(0, x), Math.Max(0, y));
        }

        /**
         * Setzt Standardwerte fuer die Talk-Raum Erstellung.
         */
        private static bool DetermineEventConversationSupport(AddinSettings defaults, out string versionText)
        {
            versionText = string.Empty;
            if (defaults == null)
            {
                return true;
            }

            versionText = defaults.LastKnownServerVersion ?? string.Empty;

            Version parsed;
            if (NextcloudVersionHelper.TryParse(versionText, out parsed))
            {
                versionText = parsed.ToString();
                return parsed.Major >= 31;
            }

            return true;
        }

        private void ApplyDefaults(string appointmentSubject)
        {
            // appointmentSubject wird bewusst ignoriert; wir verwenden den lokalisierbaren Standardtitel.
            TalkTitle = DefaultTitle;
            _titleTextBox.Text = TalkTitle;

            TalkPassword = string.Empty;
            _passwordTextBox.Text = string.Empty;

            _lobbyCheckBox.Checked = true;
            _searchCheckBox.Checked = true;

            if (_eventConversationsSupported)
            {
                _eventConversationRadio.Checked = true;
                _standardRoomRadio.Checked = false;
                SelectedRoomType = TalkRoomType.EventConversation;
            }
            else
            {
                _eventConversationRadio.Checked = false;
                _standardRoomRadio.Checked = true;
                SelectedRoomType = TalkRoomType.StandardRoom;
            }

            LobbyUntilStart = true;
            SearchVisible = true;
        }

        /**
         * Erfasst Benutzereingaben, prueft das Passwort und uebergibt die Auswahl an den Aufrufer.
         */
        private void OnOkButtonClick(object sender, EventArgs e)
        {
            TalkTitle = _titleTextBox.Text.Trim();

            TalkPassword = _passwordTextBox.Text.Trim();
            if (TalkPassword.Length > 0 && TalkPassword.Length < MinPasswordLength)
            {
                MessageBox.Show(
                    Strings.TalkPasswordTooShort,
                    Strings.DialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                _passwordTextBox.Focus();
                _passwordTextBox.SelectAll();
                return;
            }

            LobbyUntilStart = _lobbyCheckBox.Checked;
            SearchVisible = _searchCheckBox.Checked;
            SelectedRoomType = _eventConversationRadio.Checked ? TalkRoomType.EventConversation : TalkRoomType.StandardRoom;
        }

        private static Image LoadEmbeddedImage(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }
                return Image.FromStream(stream);
            }
        }
    }
}
