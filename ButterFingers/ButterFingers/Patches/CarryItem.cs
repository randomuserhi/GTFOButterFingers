using AIGraph;
using API;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using ButterFingers.BepInEx;
using FluffyUnderware.DevTools.Extensions;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;

// TODO(randomuserhi): Clean up code greatly => statically store local player etc...

namespace ButterFingers {
    internal class Cooldown {
        public static float timer = 0;
    }

    [HarmonyPatch]
    internal class CarryItem : MonoBehaviour {

        [HarmonyPatch]
        private static class Patches {
            private static System.Collections.IEnumerator DelayedEndSession() {
                // Wait for packet to send
                yield return new WaitForSeconds(1f);

                // then quit for real
                patch = false;
                RundownManager.Current.EndGameSession();
                patch = true;
            }

            private static bool patch = true;
            [HarmonyPatch(typeof(RundownManager), nameof(RundownManager.EndGameSession))]
            [HarmonyPrefix]
            private static bool EndGameSession() {
                if (patch == false) return true;

                APILogger.Debug($"CarryItem Level ended!");

                foreach (CarryItem item in instances.Values) {
                    item.ForceStop();
                }

                foreach (ResourcePack item in ResourcePack.instances.Values) {
                    item.ForceStop();
                    item.gameObject.Destroy();
                }

                foreach (Consumable item in Consumable.instances.Values) {
                    item.ForceStop();
                    item.gameObject.Destroy();
                }

                ResourcePack.instances.Clear();
                Consumable.instances.Clear();

                instances.Clear();

                PlayerManager.GetLocalPlayerAgent().StartCoroutine(DelayedEndSession().WrapToIl2Cpp());

                return false;
            }

            [HarmonyPatch(typeof(CarryItemPickup_Core), nameof(CarryItemPickup_Core.Setup))]
            [HarmonyPostfix]
            private static void Setup(CarryItemPickup_Core __instance) {
                __instance.gameObject.AddComponent<CarryItem>();
            }

            [HarmonyPatch(typeof(CarryItemPickup_Core), nameof(CarryItemPickup_Core.OnSyncStateChange))]
            [HarmonyPrefix]
            private static void Prefix_StatusChange(CarryItemPickup_Core __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
                int instance = __instance.GetInstanceID();
                if (instances.ContainsKey(instance)) {
                    instances[instance].Prefix_OnStatusChange(status, placement, player, isRecall);
                }
            }

            [HarmonyPatch(typeof(CarryItemPickup_Core), nameof(CarryItemPickup_Core.OnSyncStateChange))]
            [HarmonyPostfix]
            [HarmonyPriority(Priority.High)]
            private static void Postfix_StatusChange(CarryItemPickup_Core __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
                int instance = __instance.GetInstanceID();
                if (instances.ContainsKey(instance)) {
                    instances[instance].Postfix_OnStatusChange(status, placement, player, isRecall);
                }
            }

            // NOTE(randomuserhi): Trigger static update here, cause when item is picked up its object is deactivated so monobehaviour update doesnt run
            [HarmonyPatch(typeof(PlayerAgent), nameof(PlayerAgent.Update))]
            [HarmonyPrefix]
            private static void Update(PlayerAgent __instance) {
                if (!__instance.Owner.IsLocal || __instance.Owner.IsBot) return;
                if (Clock.Time < Cooldown.timer) return;

                foreach (CarryItem item in instances.Values) {
                    item.Footstep();
                }
            }

            [HarmonyPatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.LeaveHub))]
            [HarmonyPrefix]
            private static void LeaveHub() {
                foreach (CarryItem item in instances.Values) {
                    item.ForceStop();
                }
            }

            [HarmonyPatch(typeof(PLOC_Downed), nameof(PLOC_Downed.CommonEnter))]
            [HarmonyPrefix]
            [HarmonyWrapSafe]
            private static void Prefix_CommonEnter(PLOC_Downed __instance) {
                if (!__instance.m_owner.Owner.IsLocal || __instance.m_owner.Owner.IsBot) return;

                PlayerAgent player = PlayerManager.GetLocalPlayerAgent();
                if (PlayerBackpackManager.TryGetItem(player.Owner, InventorySlot.InLevelCarry, out BackpackItem bpItem)) {
                    if (bpItem.Instance == null) {
                        return;
                    }
                    ItemEquippable item = bpItem.Instance.Cast<ItemEquippable>();
                    if (item == null) {
                        return;
                    }
                    ItemInLevel? levelItemFromItemData = GetLevelItemFromItemData(item.Get_pItemData());
                    if (levelItemFromItemData == null) {
                        return;
                    }
                    int instance = levelItemFromItemData.GetInstanceID();
                    if (instances.ContainsKey(instance)) {
                        instances[instance].Footstep(force: true);
                    }

                    if (GuiManager.MainMenuLayer != null && GuiManager.MainMenuLayer.PageMap != null) {
                        GuiManager.MainMenuLayer.PageMap.UpdatePlayerData();
                    }
                }
            }
        }

        private static ItemInLevel? GetLevelItemFromItemData(pItemData itemData) {
            if (!PlayerBackpackManager.TryGetItemInLevelFromItemData(itemData, out var item)) {
                return null;
            }
            if (item.TryCast<ItemInLevel>() == null) {
                return null;
            }
            return item.TryCast<ItemInLevel>();
        }

        private int instance;
        private CarryItemPickup_Core? core;
        private LG_PickupItem_Sync? sync;
        private Rigidbody? rb;
        private CapsuleCollider? collider;

        private static Dictionary<int, CarryItem> instances = new Dictionary<int, CarryItem>();

        private void Start() {
            core = GetComponent<CarryItemPickup_Core>();
            sync = GetComponent<LG_PickupItem_Sync>();
            if (sync == null || core == null) return;

            instance = core.GetInstanceID();

            instances.Add(instance, this);

            core.gameObject.layer = LayerManager.LAYER_DEBRIS;

            rb = gameObject.AddComponent<Rigidbody>();
            collider = gameObject.AddComponent<CapsuleCollider>();

            rb.drag = 0.01f;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            PhysicMaterial material = new PhysicMaterial();
            material.bounciness = 0.4f;
            material.bounceCombine = PhysicMaterialCombine.Maximum;
            collider.material = material;
        }

        private Vector3 physicsPosition;
        private Quaternion physicsRotation;
        private bool prevKinematic = true;
        private PlayerAgent? carrier = null;
        private void Prefix_OnStatusChange(ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
            if (rb == null) return;
            if (player == null) return;

            if (status == ePickupItemStatus.PickedUp && placement.linkedToMachine == false) {
                carrier = player;
            } else {
                carrier = null;
            }

            bool physicsEnded = prevKinematic == false && rb.isKinematic == true;

            if (isRecall || status != ePickupItemStatus.PlacedInLevel) {
                rb.isKinematic = true;
                return;
            }

            if (rb.isKinematic) {
                if (!physicsEnded && player.GlobalID != PlayerManager.GetLocalPlayerAgent().GlobalID) {
                    rb.isKinematic = true;
                    return;
                }

                if (performSlip) {
                    performSlip = false;

                    Slip(player, placement.position + Vector3.up * 1.5f, placement.rotation, player.m_courseNode);
                }
            }

            if (rb.isKinematic == false) {
                physicsPosition = transform.position;
                physicsRotation = transform.rotation;
            }
        }

        private void Postfix_OnStatusChange(ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
            if (rb == null || core == null || sync == null) return;

            if (rb.isKinematic == false) {
                transform.position = physicsPosition;
                transform.rotation = physicsRotation;
            }

            prevKinematic = rb.isKinematic;
        }

        private AIG_CourseNode? GetNode(Vector3 position) {
            if (AIG_GeomorphNodeVolume.TryGetNode(0, Dimension.GetDimensionFromPos(position).DimensionIndex, position, out var node2) && AIG_NodeCluster.TryGetNodeCluster(node2.ClusterID, out var nodeCluster)) {
                if (nodeCluster.CourseNode == null) {
                    return null;
                }
                return nodeCluster.CourseNode;
            }
            return null;
        }

        private bool startTracking = false;
        private Vector3 prevPosition = Vector3.zero;
        private float distance = 0;
        private void Footstep(bool force = false) {
            if (sync == null || carrier == null || rb == null) return;
            if (rb.isKinematic == false) return;

            if (sync.m_stateReplicator.State.status == ePickupItemStatus.PickedUp) {
                PlayerAgent player = PlayerManager.GetLocalPlayerAgent();
                if (carrier.GlobalID == player.GlobalID && player.Inventory != null) {
                    if (player.Inventory.WieldedSlot == InventorySlot.InLevelCarry) {
                        if (startTracking == false) {
                            startTracking = true;

                            prevPosition = player.Position;
                        }

                        distance += (player.Position - prevPosition).magnitude;
                        prevPosition = player.Position;

                        if (distance > ConfigManager.DistancePerRoll || force == true) {
                            distance = 0;

                            if (UnityEngine.Random.Range(0.0f, 1.0f) < ConfigManager.HeavyItemProbability || force == true) {
                                performSlip = true;
                                PlayerBackpackManager.WantToDropItem_Local(player.Inventory.WieldedItem.Get_pItemData(), player.Position, player.Rotation);
                            }
                        }
                    }
                }
            }
        }

        private bool performSlip = false;
        private void Slip(PlayerAgent player, Vector3 position, Quaternion rotation, AIG_CourseNode node) {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            gameObject.SetActive(true);

            rb.isKinematic = false;

            Cooldown.timer = Clock.Time + ConfigManager.Cooldown;
            prevTime = Clock.Time;
            stillTimer = 0;
            startTracking = false;
            carrier = null;

            Vector3 direction = UnityEngine.Random.insideUnitSphere;
            rb.velocity = Vector3.zero;
            rb.AddForce(direction * ConfigManager.Force);

            SetTransform(position, rotation, node);
        }

        private void SetTransform(Vector3 position, Quaternion rotation, AIG_CourseNode node) {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            transform.position = position;
            transform.rotation = rotation;
            gameObject.transform.SetParent(node.gameObject.transform);
            core.m_itemCuller.MoveToNode(node.m_cullNode, transform.position);
        }

        private void OnCollisionStay() {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            if (rb.velocity.y > -0.3f && rb.velocity.y < 0.3f) {
                rb.velocity *= 0.92f;
                rb.angularVelocity *= 0.92f;
            }
        }

        private NM_NoiseData? noise;
        private float noiseDelay = 0;
        private void OnCollisionEnter() {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;
            if (ConfigManager.MakeNoise == false) return;

            if (rb.isKinematic == true) return;

            if (core.m_courseNode != null && Clock.Time > noiseDelay) {
                noiseDelay = Clock.Time + 1.5f;
                noise = new NM_NoiseData() {
                    noiseMaker = null,
                    position = transform.position,
                    radiusMin = 4,
                    radiusMax = 10,
                    yScale = 1f,
                    node = core.m_courseNode,
                    type = NM_NoiseType.Detectable,
                    includeToNeightbourAreas = true,
                    raycastFirstNode = false
                };
                NoiseManager.MakeNoise(noise);
            }
        }

        private byte oldValue = 0;
        private float prevTime = 0;
        private float stillTimer = 0;
        private float blink = 0;
        private void FixedUpdate() {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            if (rb.isKinematic == true) {
                oldValue = sync.m_stateReplicator.State.custom.byteState;
                return;
            }

            AIG_CourseNode? node = GetNode(transform.position);

            bool grounded = Physics.Raycast(transform.position, Vector3.down, 1);

            const float minimumVelocity = 1f;
            if (rb.velocity.sqrMagnitude < minimumVelocity * minimumVelocity && grounded) {
                stillTimer += Clock.Time - prevTime;
            } else {
                stillTimer = 0;
            }

            if (node != null) {
                gameObject.transform.SetParent(node.gameObject.transform);
                core.m_itemCuller.MoveToNode(node.m_cullNode, transform.position);
            }

            pItemData_Custom custom = new pItemData_Custom();
            custom.ammo = sync.m_stateReplicator.State.custom.ammo;
            custom.byteId = sync.m_stateReplicator.State.custom.byteId;
            custom.byteState = (byte)eCarryItemCustomState.Inserted_Visible_NotInteractable; // Make it non-interactable

            // Additional condition after stillTimer to stop simulating physics if cell is out of bounds
            // TODO(randomuserhi): Check falling below other dimension boundaries instead of just -2500
            if (stillTimer > 1f || transform.position.y < -2500) {
                rb.velocity = Vector3.zero;
                rb.isKinematic = true;
                custom.byteState = oldValue; // Make it interactable after stopping
            }

            if (Clock.Time > blink - 0.5f) {
                custom.byteState = oldValue;
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, PlayerManager.GetLocalPlayerAgent().Owner, position: transform.position, rotation: transform.rotation, node: node, droppedOnFloor: true, forceUpdate: true, custom: custom);
            } else {
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, PlayerManager.GetLocalPlayerAgent().Owner, position: transform.position, rotation: transform.rotation, node: node, droppedOnFloor: true, forceUpdate: true, custom: custom);
            }

            if (Clock.Time > blink) {
                blink = Clock.Time + 1f;
            }

            prevTime = Clock.Time;
        }

        private static bool allowQuitting = false;
        System.Collections.IEnumerator DelayedQuit() {
            // Wait for packet to send
            yield return new WaitForSeconds(1f);

            // then quit for real
            Environment.FailFast("Gotta go fast!");
        }

        private void OnApplicationQuit() {
            allowQuitting = true;

            ForceStop();

            if (allowQuitting) {
                Application.CancelQuit();

                StartCoroutine(DelayedQuit().WrapToIl2Cpp());
            }
        }

        private void ForceStop() {
            APILogger.Debug("CARRY ITEM FORCE STOP");

            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;
            if (rb.isKinematic == true) return;

            rb.isKinematic = true;

            pItemData_Custom custom = new pItemData_Custom();
            custom.ammo = sync.m_stateReplicator.State.custom.ammo;
            custom.byteId = sync.m_stateReplicator.State.custom.byteId;
            custom.byteState = oldValue;

            AIG_CourseNode? node = PlayerManager.GetLocalPlayerAgent().GoodNodeCluster.m_courseNode;
            node.m_nodeCluster.TryGetClosetPositionInCluster(transform.position, out var closestPosition);
            sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, PlayerManager.GetLocalPlayerAgent().Owner, position: closestPosition, rotation: transform.rotation, node: node, droppedOnFloor: true, forceUpdate: true, custom: custom);
        }
    }
}
