using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using RoR2.UI.LogBook;
using RoR2.UI;
using UnityEngine.Events;
using UnityEngine.UI;
using RoR2.Achievements;
using RoR2.Achievements.Artifacts;
using RoR2.Achievements.Croco;

namespace AchievementPins
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "prodzpod";
        public const string PluginName = "AchievementPins";
        public const string PluginVersion = "1.0.0";
        public static ManualLogSource Log;
        public static PluginInfo pluginInfo;
        public static ConfigFile Config;
        public static ConfigEntry<bool> ShowOnTab;
        public static ConfigEntry<bool> RemoveOnComplete;
        public static ConfigEntry<int> RandomPins;
        public static ConfigEntry<bool> RegenPins;
        public static List<AchievementDef> pins = new();
        public static int pinIndex = 0;
        public static List<AchievementClass> trackers = new();
        public static Dictionary<AchievementDef, BaseAchievement> defToClass = new();
        public static List<GameObject> pinObjects = new();
        public static List<AchievementDef> tempPins = new();
        public static bool invopen = false;

        public void Awake()
        {
            pluginInfo = Info;
            Log = Logger;
            Config = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, PluginGUID + ".cfg"), true);
            ShowOnTab = Config.Bind("General", "Hide Without Inventory Key", false, "Hide pinlist until tab is pressed.");
            RemoveOnComplete = Config.Bind("General", "Remove on Complete", false, "Remove pin when it's completed.");
            RandomPins = Config.Bind("General", "Pin Random Achievements", 0, "Every run will have random pins up to that amount. Manual pins overrides it.");
            RegenPins = Config.Bind("General", "Regenerate Pins", false, "Automatically adds a new pin upon clearing one.");

            On.RoR2.UI.ScoreboardController.OnEnable += (orig, self) => { orig(self); invopen = true; };
            On.RoR2.UI.ScoreboardController.OnDisable += (orig, self) => { orig(self); invopen = false; };
            On.RoR2.UI.LogBook.CategoryDef.InitializeChallenge += (orig, obj, entry, status, profile) =>
            {
                orig(obj, entry, status, profile);
                if (status == EntryStatus.Unencountered)
                {
                    HGButton button = obj.GetComponent<HGButton>();
                    if (status == EntryStatus.Unencountered)
                    {
                        button.disableGamepadClick = false;
                        button.disablePointerClick = false;
                        if (pins.Contains((AchievementDef)entry.extraData))
                        {
                            button.transform.Find("BGImage").Find("HasBeenUnlocked").gameObject.SetActive(true);
                            button.transform.Find("BGImage").Find("HasBeenUnlocked").GetComponent<Image>().enabled = false;
                        }
                        button.onClick.AddListener(new UnityAction(ToggleAchievement(button, (AchievementDef)entry.extraData)));
                    }
                }
            };
            IL.RoR2.UI.LogBook.LogBookController.BuildEntriesPage += (il) =>
            {
                ILCursor c = new(il);
                c.GotoNext(x => x.MatchLdloc(13), x => x.MatchLdcI4(4));
                c.Index++;
                c.Remove();
                c.Emit(OpCodes.Ldc_I4_3);
            };
            ObjectivePanelController.collectObjectiveSources += (master, list) =>
            {
                pinIndex = 0;
                trackers.Clear();
                foreach (var pin in pinObjects) list.Add(new ObjectivePanelController.ObjectiveSourceDescriptor
                {
                    source = pin,
                    master = master,
                    objectiveType = typeof(AchievementClass)
                });
            };
            On.RoR2.UserAchievementManager.OnInstall += (orig, self, user) =>
            {
                Log.LogDebug("Adding Achievements...");
                defToClass.Clear();
                orig(self, user);
                Log.LogDebug("Achievements: " + defToClass.Count);
            };
            On.RoR2.Achievements.BaseAchievement.OnInstall += (orig, self) => { orig(self); defToClass.Add(self.achievementDef, self); };
            On.RoR2.Achievements.BaseAchievement.OnUninstall += (orig, self) => { orig(self); RemovePin(self.achievementDef); };
            On.RoR2.Achievements.BaseAchievement.Grant += (orig, self) => { orig(self); RemovePin(self.achievementDef); };
            Run.onRunStartGlobal += (run) =>
            {
                pins = pins.Where(x => defToClass.ContainsKey(x) && BetterBodyCheck(defToClass[x])).ToList();
                tempPins.Clear();
                Log.LogDebug("Fetching Random Pins");
                for (var i = pins.Count; i < RandomPins.Value; i++)
                {
                    AchievementDef[] defs = defToClass.Keys.Except(pins).Where(x => BetterBodyCheck(defToClass[x]) && TrialCheck(x)).ToArray();
                    if (defs.Length == 0) break;
                    AchievementDef def = RoR2Application.rng.NextElementUniform(defs);
                    pins.Add(def);
                    tempPins.Add(def);
                }
                foreach (var pin in pinObjects) Destroy(pin);
                pinObjects.Clear();
                foreach (var pin in pins)
                {
                    GameObject ret = new GameObject("Achievement Pin Holder");
                    pinObjects.Add(ret);
                    ret.transform.parent = Run.instance.transform;
                }
            };
            Run.onRunDestroyGlobal += _ => pins.RemoveAll(x => tempPins.Contains(x));
        }

        public static void RemovePin(AchievementDef def)
        {
            int index = pins.IndexOf(def);
            if (index == -1) return;
            pins.RemoveAt(index);
            tempPins.Remove(def);
            Destroy(pinObjects[index]);
            pinObjects.RemoveAt(index);
            if (RegenPins.Value)
            {
                AchievementDef[] defs = defToClass.Keys.Except(pins).Where(x => BetterBodyCheck(defToClass[x]) && TrialCheck(x)).ToArray();
                if (defs.Length == 0) return;
                AchievementDef def2 = RoR2Application.rng.NextElementUniform(defs);
                pins.Add(def2);
                tempPins.Add(def2);
                GameObject ret = new GameObject("Achievement Pin Holder");
                pinObjects.Add(ret);
                ret.transform.parent = Run.instance.transform;
            }
        }

        public static bool BetterBodyCheck(BaseAchievement bc)
        {
            if (bc is CrocoTotalInfectionsMilestoneAchievement || bc is CrocoKillWeakEnemiesMilestoneAchievement) return NetworkUser.readOnlyInstancesList.First(x => x.isLocalPlayer).master.bodyPrefab.name == "CrocoBody";
            return bc.LookUpRequiredBodyIndex() == BodyIndex.None || BodyCatalog.GetBodyName(bc.LookUpRequiredBodyIndex()) == NetworkUser.readOnlyInstancesList.First(x => x.isLocalPlayer).master.bodyPrefab.name;
        }

        public static bool TrialCheck(AchievementDef x)
        {
            if (!pins.Exists(x => defToClass[x] is BaseObtainArtifactAchievement)) return true;
            return defToClass[x] is not BaseObtainArtifactAchievement;
        }

        public static Action ToggleAchievement(HGButton self, AchievementDef def)
        {
            return () =>
            {
                if (pins.Contains(def)) UnpinAchievement(self, def);
                else PinAchievement(self, def);
            };
        }
        public static void PinAchievement(HGButton self, AchievementDef def)
        {
            pins.Add(def);
            self.transform.Find("BGImage").Find("HasBeenUnlocked").gameObject.SetActive(true);
            self.transform.Find("BGImage").Find("HasBeenUnlocked").GetComponent<Image>().enabled = false;
            Util.PlaySound("Play_UI_skill_unlock", RoR2Application.instance.gameObject);
        }
        public static void UnpinAchievement(HGButton self, AchievementDef def)
        {
            pins.Remove(def);
            self.transform.Find("BGImage").Find("HasBeenUnlocked").gameObject.SetActive(false);
            Util.PlaySound("Play_UI_achievementUnlock", RoR2Application.instance.gameObject);
        }

        public class AchievementClass : ObjectivePanelController.ObjectiveTracker
        {
            public bool _invopen = false;
            public AchievementDef def;
            public AchievementClass()
            {
                def = pins[pinIndex];
                baseToken = pins[pinIndex].nameToken;
                pinIndex++;
                trackers.Add(this);
                _invopen = !invopen;
            }
            public override string GenerateString()
            {
                return Language.GetString(invopen ? def.descriptionToken : def.nameToken);
            }

            public override bool IsDirty()
            {
                if (invopen != _invopen)
                {
                    _invopen = invopen;
                    return true;
                }
                return false;
            }
        }
    }
}
