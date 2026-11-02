using System;
using System.Collections;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        DeviceAuthClient deviceAuth;
        bool accountInitialized;
        string accountConfigurationStatus = "온라인 계정 연결이 설정되지 않았습니다.";
        bool accountLinkingNotice;
        bool firstAccountEntryRunning;
        public event Action AccountCredentialsChanged;
        public string AuthenticatedAccountId => deviceAuth?.AccountId;
        public string AccountAccessToken => deviceAuth?.AccessToken;
#if UNITY_EDITOR
        // Capture fixtures never create credentials or contact the production endpoint.
        public void SetAccountCaptureFixture(DeviceAuthClient fixture)
        {
            deviceAuth = fixture ?? throw new ArgumentNullException(nameof(fixture));
            accountInitialized = true;
        }
        public void ShowAccountNoticeCaptureFixture() => ShowAccountNotice(false);
        public void ShowAccountSettingsCaptureFixture() => ShowAccountSettings();
#endif
        static string AccountEndpoint()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--account-server") return args[i + 1];
            return Environment.GetEnvironmentVariable("SOW_ACCOUNT_SERVER");
        }
        void InitializeAccountAccess()
        {
            if (accountInitialized) return; accountInitialized = true;
            var endpoint = AccountEndpoint(); if (string.IsNullOrWhiteSpace(endpoint)) return;
            try
            {
                bool allowLoopback = Application.isEditor || UnityEngine.Debug.isDebugBuild;
                var transport = new DeviceAuthHttp(endpoint, allowLoopback);
                deviceAuth = new DeviceAuthClient(new NativeDeviceIdentityVault(endpoint.TrimEnd('/')), transport);
                deviceAuth.CredentialsChanged += () => AccountCredentialsChanged?.Invoke();
            }
            catch (Exception) { accountConfigurationStatus = "온라인 계정 연결 설정을 확인하지 못했습니다."; }
        }
        void EnterPortraitFromTitle()
        {
            if (preferences.IntroductionSeen) { ShowPortraitEstate(); return; }
            if (!string.IsNullOrWhiteSpace(AccountEndpoint())) { ShowAccountNotice(false); return; }
            StartRun();
        }
        void ShowAccountNotice(bool linking)
        {
            accountLinkingNotice = linking; screen = UiScreen.AccountNotice; MenuOpen = true; ClearUi(); hud = null;
            AccountAccessPanels.Notice(Ui, linking, linking ? (Action)ShowAccountSettings : () => StartCoroutine(FirstAccountEntry()),
                linking ? (Action)ShowAccountSettings : ShowPortraitTitle);
        }
        void ShowAccountSettings()
        {
            InitializeAccountAccess(); screen = UiScreen.AccountSettings; MenuOpen = true; ClearUi(); hud = null;
            AccountAccessPanels.Settings(Ui, deviceAuth, accountConfigurationStatus,
                () => StartCoroutine(AccountOperation(deviceAuth.HasDeviceAccount ? deviceAuth.SignIn(false) : deviceAuth.CreateGuest())),
                () => ShowAccountNotice(true), ShowAccountDeletion,
                () => StartCoroutine(AccountOperation(deviceAuth.SignIn(true))), ReturnAccountSettings);
        }
        void ShowAccountDeletion()
        {
            if (deviceAuth?.Connected != true || deviceAuth.Busy) return;
            screen = UiScreen.AccountDeletion; ClearUi();
            AccountAccessPanels.ConfirmDeletion(Ui, () => StartCoroutine(AccountOperation(deviceAuth.DeleteConfirmed())), ShowAccountSettings);
        }
        void ReturnAccountSettings()
        {
            var origin = settingsOrigin;
            ShowSettings(Session != null && !finished);
            settingsOrigin = origin;
        }
        IEnumerator FirstAccountEntry()
        {
            if (firstAccountEntryRunning) yield break;
            firstAccountEntryRunning = true;
            try
            {
                InitializeAccountAccess();
                if (deviceAuth == null || deviceAuth.DeletionPending) { ShowAccountSettings(); yield break; }
                if (!deviceAuth.Connected)
                    yield return deviceAuth.HasDeviceAccount ? deviceAuth.SignIn(false) : deviceAuth.CreateGuest();
                // Leaving the notice is a cancellation of navigation, not a cancellation of a submitted server request.
                if (screen != UiScreen.AccountNotice || accountLinkingNotice) yield break;
                if (deviceAuth.Connected) StartRun(); else ShowAccountSettings();
            }
            finally { firstAccountEntryRunning = false; }
        }
        IEnumerator AccountOperation(IEnumerator operation)
        {
            var running = StartCoroutine(operation);
            ShowAccountSettings();
            yield return running;
            if (screen == UiScreen.AccountSettings) ShowAccountSettings();
        }
    }
}
