using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        FeelExperimentOptions activeFeelRules;
        bool activeHitFeedback, activeHeroRig;
        // The knight sprite sheets replace the cutout B rig in play (#268); the B rig stays only where a comparison or the
        // crowd benchmark asks for it explicitly, or when the hero has no sheet roles.
        bool explicitHeroRig;
        public FeelExperimentOptions ActiveFeelRules => activeFeelRules;
        public bool ActiveHitFeedback => activeHitFeedback;
        public bool ActiveHeroRig => activeHeroRig;

        void SnapshotFeelPreferences()
        {
            activeFeelRules = IsWave ? preferences.SnapshotFeel() : null;
            activeHitFeedback = IsWave && preferences.FeelFeedback;
            activeHeroRig = IsWave && preferences.HeroRigPreview;
            explicitHeroRig = false;
            var trial = FeelTrialAutomation.Active?.Options;
            if (trial != null && IsWave)
            {
                activeFeelRules = trial.Rules;
                activeHitFeedback = trial.Feedback;
            }
            var comparison = AutoplayCapture.Active?.FeelComparison;
            if(comparison != null && IsWave)
            {
                activeFeelRules = comparison.RequestedRulesOn ? new FeelExperimentOptions(true,true,true) : comparison.WalkFast ? new FeelExperimentOptions(true,false,false) : null;
                activeHitFeedback = comparison.RequestedFeedback;
                activeHeroRig = comparison.RequestedHeroB;
                explicitHeroRig = comparison.RequestedHeroB;
            }
            if(IsWave && (NormalGcProbe.PreferencesPrefix != null || AutoplayCapture.Active?.FieldVideo != null))
            {
                activeFeelRules = null;
                activeHitFeedback = false;
                activeHeroRig = true;
            }
        }

        public void FinishFeelTrialRecording()
        {
            if (FeelTrialAutomation.Active == null || Session == null)
                throw new System.InvalidOperationException("Only an active native feel trial may close its recording.");
            if (Session.View.Status == RunStatus.Completed && !finished) CompleteRun();
            StopRecording();
        }

        void AddFeelSettings(Transform panel)
        {
            if (!IsWave) return;
            Ui.Label(panel,"걷기와 피격 실험 · 다음 출정부터",Game.View.UiTokens.Heading,56);
            Ui.Toggle(panel,"빠른 걸음 ×1.3",preferences.FeelSpeed,v=>{preferences.FeelSpeed=v;preferences.Save();});
            Ui.Toggle(panel,"접촉 즉시 피격 · 잠깐 무적과 밀려남",preferences.FeelContact,v=>{preferences.FeelContact=v;preferences.Save();});
            Ui.Toggle(panel,"체력 100 · 한 대 5~15",preferences.FeelHealth,v=>{preferences.FeelHealth=v;preferences.Save();});
            Ui.Toggle(panel,"피격 잔상 · 붉은 테 · 피해 숫자",preferences.FeelFeedback,v=>{preferences.FeelFeedback=v;preferences.Save();});
            Ui.Toggle(panel,"걷기 B 시안 · 부위별 애니메이션",preferences.HeroRigPreview,v=>{preferences.HeroRigPreview=v;preferences.Save();});
        }
    }
}
