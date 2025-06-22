using BepInEx;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;
using Steamworks.Data;
using BepInEx.Configuration;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Object = UnityEngine.Object;
using System;

namespace Size2Fix;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin {
  public const string PLUGIN_GUID = "com.earthlingOnFire.Size2Fix";
  public const string PLUGIN_NAME = "Size 2 Fix";
  public const string PLUGIN_VERSION = "1.0.0";

  internal static ConfigEntry<bool> hasCaughtSize2;
  internal static ConfigEntry<int> probability;

  public void Awake() {
    probability = this.Config.Bind(
        "General", 
        "Probability",
        10,
        new ConfigDescription("Probability of a fish's size being 2.", new AcceptableValueRange<int>(0, 100))
        );
    hasCaughtSize2 = this.Config.Bind(
        "General",
        "Has Caught Size 2",
        false,
        "Whether or not a size 2 fish has been caught yet. Will be set to \"true\" when a fish of size 2 is caught."
        );
  }

  public void Start() {
    new Harmony(PLUGIN_GUID).PatchAll();
  }
}

[HarmonyPatch]
static class Patches {
  private static readonly Vector3 fishSize2 = Vector3.one * 1.2f;
  private static readonly Vector3 sharkSize2 = Vector3.one * 0.8f;
  private static readonly Vector3 dopeSize2 = Vector3.one * 0.8f;
  private static readonly Vector3 fishWorldSize2 = Vector3.one * 2f;

  [HarmonyPostfix]
  [HarmonyPatch(typeof(LeaderboardController), nameof(LeaderboardController.GetFishScores))]
  private static async void LeaderboardController_GetFishScores_Postfix(Task<LeaderboardEntry[]> __result, LeaderboardType type) {
    LeaderboardEntry[] leaderboard = await __result;
    if (leaderboard == null) return;
    if (!Plugin.hasCaughtSize2.Value) return;

    int targetIndex = Array.FindIndex(leaderboard, e => e.Score == 1);
    if (targetIndex < 0) return;

    int userIndex = Array.FindIndex(leaderboard, e => e.User.IsMe);

    if (userIndex >= 0) {
      if (leaderboard[userIndex].Score == 2) {
        return;
      }
      Friend otherUser = leaderboard[targetIndex].User;
      leaderboard[userIndex].User = otherUser;
    } else {
      targetIndex = 2;
    }

    leaderboard[targetIndex].User = new Friend(SteamClient.SteamId);
    leaderboard[targetIndex].Score = 2;
    leaderboard[targetIndex].GlobalRank = 3;
  }

  private static bool NextFishIsSize2() => UnityEngine.Random.Range(0, 100) < Plugin.probability.Value;

  [HarmonyPrefix]
  [HarmonyPatch(typeof(FishBait), nameof(FishBait.CatchFish))]
  private static void FishBait_CatchFish_Prefix(FishBait __instance, out bool __state) {
    __state = !__instance.returnToRod;
  }

  [HarmonyPostfix]
  [HarmonyPatch(typeof(FishBait), nameof(FishBait.CatchFish))]
  private static void FishBait_CatchFish_Postfix(FishBait __instance, bool __state) {
    if (!__state) return;
    if (NextFishIsSize2()) {
      GameObject fish = __instance.spawnedFish.gameObject;
      fish.name += " (size 2)";
      fish.transform.localScale *= 2;
    }
  }

  [HarmonyPrefix]
  [HarmonyPatch(typeof(FishBait), nameof(FishBait.ReturnAnim))]
  private static bool FishBait_ReturnAnim_Prefix(FishBait __instance) {
    if (__instance.flyProgress < 1f) return true;

    GameObject spawnedFish = __instance.spawnedFish.gameObject;
    FishingRodWeapon rod = __instance.sourceWeapon;

    __instance.flyProgress = 1f;
    rod.animator.SetTrigger("Idle");
    FishingHUD.Instance.ShowFishCaught(show: true, rod.hookedFishe.fish);
    GameObject newFish = FishingRodWeapon.CreateFishPickup(
      rod.fishPickupTemplate, 
      rod.hookedFishe.fish, 
      grab: true
    );
    rod.ResetFishing();
    rod.pullSound.Stop();
    Object.Destroy(spawnedFish.gameObject);
    try {
      LeaderboardController.Instance.SubmitFishSize(SteamController.FishSizeMulti);
    } catch { }

    UpdateSizeText(spawnedFish);

    if (!spawnedFish.name.Contains("size 2")) return true;

    Plugin.hasCaughtSize2.Value = true;

    GameObject fish = newFish.FindNonDummyChild();
    fish.name += " (size 2)";

    Vector3 scale;
    if (fish.name.Contains("Dope")) {
      scale = dopeSize2;
    } else if (fish.name.Contains("Shark")) {
      scale = sharkSize2;
    } else {
      scale = fishSize2;
    }
    fish.GetComponentInParent<ItemIdentifier>().putDownScale = scale;
    newFish.transform.localScale = scale;

    GameObject.Find("Fish Scores")?.GetComponent<FishLeaderboard>()?.Fetch();

    return false;
  }

  private static void UpdateSizeText(GameObject fish) {
    GameObject sizeObj = GameObject.Find("FishingCanvas/Fish Caught/Fish Size Text");
    Text sizeText = sizeObj.GetComponent<Text>();
    if (fish.name.Contains("size 2")) sizeText.text = "SIZE: 2";
    else sizeText.text = "SIZE: 1";
  }

  [HarmonyPostfix]
  [HarmonyPatch(typeof(Punch), nameof(Punch.ForceHold))]
  private static void Punch_ForceHold_Postfix(Punch __instance) {
    ItemIdentifier item = __instance.heldItem;
    if (item.transform.childCount <= 0) return;

    Transform fish = item.transform.GetChild(0);
    if (fish.name.Contains("Cooked Fish")) {
      UpdateSizeText(fish.gameObject);
    }
  }

  [HarmonyPostfix]
  [HarmonyPatch(typeof(Punch), nameof(Punch.ResetHeldItemPosition))]
  private static void Punch_ResetHeldItemPosition_Postfix(Punch __instance) {
    ItemIdentifier item = __instance.heldItem;
    if (item.IsSize2()) {
      item.transform.localScale = item.putDownScale;
    }
  }

  [HarmonyPrefix]
  [HarmonyPatch(typeof(Punch), nameof(Punch.ForceThrow))]
  private static void Punch_ForceThrow_Prefix(out ItemIdentifier __state, Punch __instance) {
    ItemIdentifier item = __instance.heldItem;
    if (item.IsSize2()) __state = item;
    else __state = null;
  }

  [HarmonyPostfix]
  [HarmonyPatch(typeof(Punch), nameof(Punch.ForceThrow))]
  private static void Punch_ForceThrow_Postfix(ItemIdentifier __state) {
    if (__state) {
      __state.transform.localScale = fishWorldSize2;
    }
  }

  [HarmonyReversePatch]
  [HarmonyPatch(typeof(Component), "transform", MethodType.Getter)]
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static Transform MonoBehaviourTransform(Component instance) {
    return instance.transform;
  }

  [HarmonyPrefix]
  [HarmonyPatch(typeof(FishCooker), nameof(FishCooker.OnTriggerEnter))]
  private static bool FishCooker_OnTriggerEnter_Prefix(Collider other, FishCooker __instance) {
    FishCooker cooker = __instance;
    Vector3 putDownScale = cooker.fishPickupTemplate.putDownScale;

    bool size2flag = false;
    for (int i = 0; i < other.transform.childCount; i++) {
      GameObject child = other.transform.GetChild(i).gameObject;
      if (child.name.Contains("size 2")) {
        putDownScale = fishSize2;
        size2flag = true;
        break;
      }
    }

    ItemIdentifier fishPickupTemplate = Object.Instantiate(cooker.fishPickupTemplate);
    fishPickupTemplate.putDownScale = putDownScale;

    if (!other.TryGetComponent<FishObjectReference>(out var inputFishRef)) {
      return false;
    }

    _ = FishManager.Instance.recognizedFishes[cooker.cookedFish];
    GameObject outputFish = FishingRodWeapon.CreateFishPickup(
      fishPickupTemplate,
      inputFish.canBeCooked ? cooker.cookedFish : cooker.failedFish,
      grab: false,
      unlock: false
    );
    if (!inputFish.canBeCooked) {
      HudMessageReceiver.Instance.SendHudMessage("Cooking failed.");
    }

    Vector3 cookerPos = MonoBehaviourTransform(cooker).position;
    Vector3 towardsPlayer = Vector3.Normalize(NewMovement.Instance.transform.position - cookerPos);

    outputFish.transform.SetPositionAndRotation(cookerPos, Quaternion.identity);
    outputFish.GetComponent<Rigidbody>().velocity = towardsPlayer * 18f + Vector3.up * 10f;

    if (size2flag) {
      if (outputFish.FindNonDummyChild() is GameObject child) {
        child.name += " (size 2)";
        outputFish.transform.localScale = fishWorldSize2;
      }
    }

    Object.Instantiate(cooker.cookedSound, cookerPos, Quaternion.identity);
    if (cooker.cookedParticles) {
      Object.Instantiate(cooker.cookedParticles, cookerPos, Quaternion.identity);
    }
    Object.Destroy(inputFishRef.gameObject);

    return false;
  }
}

static class Extensions {
  public static bool IsSize2(this ItemIdentifier item) => (
    item != null && 
    item.transform.childCount > 0 && 
    item.transform.GetChild(0).name.Contains("size 2")
  );

  public static GameObject FindNonDummyChild(this GameObject obj) {
    Transform t = obj.transform;
    for (int i = 0; i < t.childCount; i++) {
      GameObject child = t.GetChild(i).gameObject;
      if (child.name != "Dummy Object") {
        return child;
      }
    }
    return null;
  }
}
