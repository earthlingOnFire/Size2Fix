using System;
using BepInEx;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Steamworks.Data;
using System.Reflection;
using BepInEx.Configuration;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Size2Fix;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{	
  public const string PLUGIN_GUID = "com.earthlingOnFire.Size2Fix";
  public const string PLUGIN_NAME = "Size 2 Fix";
  public const string PLUGIN_VERSION = "1.0.0";

  internal static Plugin plugin;
  internal static ConfigFile config;
  internal static ConfigEntry<bool> hasCaughtSize2;
  internal static int probability;

  private void Awake()
  {
    plugin = this;
    config = this.Config;
    SetUpConfig();
  }

  private void SetUpConfig() {
    probability = config.Bind<int>(
        "General", 
        "Probability",
        10,
        new ConfigDescription(
          "Probability of a fish's size being 2.",
          new AcceptableValueRange<int>(0, 100)
          )
        ).Value;
    hasCaughtSize2 = config.Bind<bool>(
        "General",
        "Has Caught Size 2",
        false,
        new ConfigDescription(
          "Whether or not a size 2 fish has been caught yet. Will be set to \"true\" when a fish of size 2 is caught.",
          new AcceptableValueList<bool>(true, false)
          )
        );
  }

  private void Start()
  {
    new Harmony(PLUGIN_GUID).PatchAll();
  }

  [HarmonyPatch]
  public static class Patches {

    private static Vector3 fishSize2 = new Vector3(1.2f, 1.2f, 1.2f);
    private static Vector3 sharkSize2 = new Vector3(0.8f, 0.8f, 0.8f);
    private static Vector3 dopeSize2 = new Vector3(0.8f, 0.8f, 0.8f);
    private static Vector3 fishWorldSize2 = new Vector3(2f, 2f, 2f);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LeaderboardController), nameof(LeaderboardController.GetFishScores))]
    private static async void LeaderboardController_GetFishScores_Postfix(Task<LeaderboardEntry[]> __result, LeaderboardType type) {
      LeaderboardEntry[] leaderboard = await __result;
      if (leaderboard == null) return;
      if (hasCaughtSize2.Value == true) {
        Friend user = new Friend(SteamClient.SteamId);
        int userIndex = -1;
        int targetIndex = -1;
        for (int i = 0; i < leaderboard.Length; i++) {
          LeaderboardEntry entry = leaderboard[i];
          if (entry.Score == 1 && targetIndex == -1) {
            targetIndex = i;
          }
          if (entry.User.IsMe) {
            userIndex = i;
            if (entry.Score == 2) return;
          }
        }
        if (targetIndex == -1) return;
        if (userIndex != -1) {
          Friend otherUser = leaderboard[targetIndex].User;
          leaderboard[userIndex].User = otherUser;
        } else {
          targetIndex = 2;
        }
        leaderboard[targetIndex].User = user;
        leaderboard[targetIndex].Score = 2;
        leaderboard[targetIndex].GlobalRank = 3;
      }
    }

    private static bool RollTheDice() {
      int randomInt = UnityEngine.Random.Range(0, 100);
      if(randomInt < probability) return true;
      else return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FishBait), nameof(FishBait.CatchFish))]
    private static void FishBait_CatchFish_Prefix(FishBait __instance, out bool __state) {
      BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
      FieldInfo returnToRodField = typeof(FishBait).GetField("returnToRod", bindingFlags);
      bool returnToRod = (bool)returnToRodField.GetValue(__instance);
      if (!returnToRod) __state = true;
      else __state = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FishBait), nameof(FishBait.CatchFish))]
    private static void FishBait_CatchFish_Postfix(FishBait __instance, bool __state) {
      if (__state == true && RollTheDice() == true) {
        BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo spawnedFishField = typeof(FishBait).GetField("spawnedFish", bindingFlags);
        GameObject fish = ((Transform)spawnedFishField.GetValue(__instance)).gameObject;
        fish.name = fish.name + " (size 2)";
        Vector3 scale = fish.transform.localScale;
        Vector3 newScale = new Vector3(scale.x * 2, scale.y * 2, scale.z * 2);
        fish.transform.localScale = newScale;
      }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FishBait), "ReturnAnim")]
    private static bool FishBait_ReturnAnim_Prefix(FishBait __instance) {
      if (__instance.flyProgress >= 1f) {
        BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        FieldInfo spawnedFishField = typeof(FishBait).GetField("spawnedFish", bindingFlags);
        GameObject spawnedFish = ((Transform)spawnedFishField.GetValue(__instance)).gameObject;
        FieldInfo sourceWeaponField = typeof(FishBait).GetField("sourceWeapon", bindingFlags);
        FishingRodWeapon sourceWeapon = (FishingRodWeapon)sourceWeaponField.GetValue(__instance);
        FieldInfo animatorField = typeof(FishingRodWeapon).GetField("animator", bindingFlags);
        Animator animator = (Animator)animatorField.GetValue(sourceWeapon);
        FieldInfo hookedFisheField = typeof(FishingRodWeapon).GetField("hookedFishe", bindingFlags);
        FishDescriptor hookedFishe = (FishDescriptor)hookedFisheField.GetValue(sourceWeapon);
        MethodInfo resetFishingMethod = typeof(FishingRodWeapon).GetMethod("ResetFishing", bindingFlags);
        FieldInfo fishPickupTemplateField = typeof(FishingRodWeapon).GetField("fishPickupTemplate", bindingFlags);
        ItemIdentifier fishPickupTemplate = (ItemIdentifier)fishPickupTemplateField.GetValue(sourceWeapon);

        __instance.flyProgress = 1f;

        animator.SetTrigger(Animator.StringToHash("Idle"));
        MonoSingleton<FishingHUD>.Instance.ShowFishCaught(show: true, hookedFishe.fish);
        GameObject newFish = FishingRodWeapon.CreateFishPickup(fishPickupTemplate, hookedFishe.fish, grab: true);
        resetFishingMethod.Invoke(sourceWeapon, null);

        sourceWeapon.pullSound.Stop();
        UnityEngine.Object.Destroy(spawnedFish.gameObject);
        try {
          MonoSingleton<LeaderboardController>.Instance.SubmitFishSize(SteamController.FishSizeMulti);
        } catch {} 

        if (spawnedFish.name.Contains("size 2")) { 
          GameObject fishSize = GameObject.Find("FishingCanvas/Fish Caught/Fish Size Text");
          Text fishSizeText = fishSize.GetComponent<Text>();
          fishSizeText.text = "SIZE: 2";
          hasCaughtSize2.Value = true;

          GameObject fish = null;
          for (int i = 0; i < newFish.transform.childCount; i++) {
            GameObject child = newFish.transform.GetChild(i).gameObject;
            if (child.name != "Dummy Object") {
              fish = child;
              break;
            }
          }
          fish.name = fish.name + " (size 2)";
          ItemIdentifier fishID = fish.GetComponentInParent<ItemIdentifier>();
          if (fish.name.Contains("Dope")) fishID.putDownScale = dopeSize2;
          else if (fish.name.Contains("Shark")) fishID.putDownScale = sharkSize2;
          else fishID.putDownScale = fishSize2;
          newFish.transform.localScale = fishID.putDownScale;

          GameObject scores = GameObject.Find("Fish Scores");
          if (scores != null && scores.TryGetComponent<FishLeaderboard>(out FishLeaderboard fishLeaderboard)) {
            MethodInfo fetchMethod = typeof(FishLeaderboard).GetMethod("Fetch", bindingFlags);
            fetchMethod.Invoke(fishLeaderboard, null);
          }
          return false;
        } else {
          GameObject fishSize = GameObject.Find("FishingCanvas/Fish Caught/Fish Size Text");
          Text fishSizeText = fishSize.GetComponent<Text>();
          fishSizeText.text = "SIZE: 1";
        }
      }
      return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Punch), nameof(Punch.ForceHold))]
    private static void Punch_ForceHold_Postfix(Punch __instance) {
      ItemIdentifier heldItem = __instance.heldItem;
      if (heldItem.transform.childCount > 0) {
        Transform fish = heldItem.transform.GetChild(0);
        if (fish.name.Contains("Cooked Fish")) {
          GameObject fishSize = GameObject.Find("FishingCanvas/Fish Caught/Fish Size Text");
          Text fishSizeText = fishSize.GetComponent<Text>();
          if (fish.name.Contains("size 2")) {
            fishSizeText.text = "SIZE: 2";
          } else {
            fishSizeText.text = "SIZE: 1";
          }
        }
      }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Punch), nameof(Punch.ResetHeldItemPosition))]
    private static void Punch_ResetHeldItemPosition_Postfix(Punch __instance) {
      ItemIdentifier heldItem = __instance.heldItem;
      if (heldItem.transform.childCount > 0 && heldItem.transform.GetChild(0).name.Contains("size 2")) {
        heldItem.transform.localScale = heldItem.putDownScale;
      }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Punch), nameof(Punch.ForceThrow))]
    private static void Punch_ForceThrow_Prefix(out ItemIdentifier __state, Punch __instance) {
      ItemIdentifier heldItem = __instance.heldItem;
      __state = null;
      if (!heldItem) return;
      if (heldItem.transform.childCount > 0 && heldItem.transform.GetChild(0).name.Contains("size 2")) {
        __state = heldItem;
      }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Punch), nameof(Punch.ForceThrow))]
    private static void Punch_ForceThrow_Postfix(ItemIdentifier __state) {
      ItemIdentifier size2Fish = __state;
      if (size2Fish) {
        size2Fish.transform.localScale = fishWorldSize2;
      }
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(Component), "transform", MethodType.Getter)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Transform MonoBehaviourTransform(Component instance)
    {
      return instance.transform;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FishCooker), "OnTriggerEnter")]
    private static bool FishCooker_OnTriggerEnter_Prefix(
        Collider other,
        bool ___unusable,
        TimeSince ___timeSinceLastError,
        ItemIdentifier ___fishPickupTemplate,
        FishObject ___cookedFish,
        FishObject ___failedFish,
        GameObject ___cookedSound,
        GameObject ___cookedParticles,
        FishCooker __instance
        ) {
      Vector3 putDownScale = ___fishPickupTemplate.putDownScale;
      bool size2flag = false;
      for (int i = 0; i < other.transform.childCount; i++) {
        GameObject child = other.transform.GetChild(i).gameObject;
        if (child.name.Contains("size 2")) {
          putDownScale = fishSize2;
          size2flag = true;
          break;
        }
      }
      bool unusable = ___unusable;
      TimeSince timeSinceLastError = ___timeSinceLastError;
      ItemIdentifier fishPickupTemplate = UnityEngine.Object.Instantiate(___fishPickupTemplate);
      fishPickupTemplate.putDownScale = putDownScale;
      FishObject cookedFish = ___cookedFish;
      FishObject failedFish = ___failedFish;
      GameObject cookedSound = ___cookedSound;
      GameObject cookedParticles = ___cookedParticles;

      if (!other.TryGetComponent<FishObjectReference>(out var component))
      {
        return false;
      }
      if (unusable)
      {
        if ((float)timeSinceLastError > 2f)
        {
          timeSinceLastError = 0f;
          MonoSingleton<HudMessageReceiver>.Instance.SendHudMessage("Too small for this fish.\n:^(");
        }
      }
      else if (!(component.fishObject == cookedFish) && !(component.fishObject == failedFish))
      {
        _ = MonoSingleton<FishManager>.Instance.recognizedFishes[cookedFish];
        GameObject obj = FishingRodWeapon.CreateFishPickup(fishPickupTemplate, component.fishObject.canBeCooked ? cookedFish : failedFish, grab: false, unlock: false);
        if (!component.fishObject.canBeCooked)
        {
          MonoSingleton<HudMessageReceiver>.Instance.SendHudMessage("Cooking failed.");
        }
        obj.transform.SetPositionAndRotation(MonoBehaviourTransform(__instance).position, Quaternion.identity);
        obj.GetComponent<Rigidbody>().velocity = (MonoSingleton<NewMovement>.Instance.transform.position - MonoBehaviourTransform(__instance).position).normalized * 18f + Vector3.up * 10f;

        if (size2flag) {
          for (int i = 0; i < obj.transform.childCount; i++) {
            GameObject child = obj.transform.GetChild(i).gameObject;
            if (child.name != "Dummy Object") {
              child.name = child.name + " (size 2)";
              obj.transform.localScale = fishWorldSize2;
              break;
            }
          }
        }

        UnityEngine.Object.Instantiate(cookedSound, MonoBehaviourTransform(__instance).position, Quaternion.identity);
        if ((bool)cookedParticles)
        {
          UnityEngine.Object.Instantiate(cookedParticles, MonoBehaviourTransform(__instance).position, Quaternion.identity);
        }
        UnityEngine.Object.Destroy(component.gameObject);
      }
      return false;
    }
  }
}
