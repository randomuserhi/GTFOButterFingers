using AIGraph;
using API;
using HarmonyLib;
using LevelGeneration;
using Player;
using UnityEngine;

// TODO(randomuserhi): Clean up code greatly => statically store local player etc...

namespace ButterFingers {
    [HarmonyPatch]
    internal class CarryItem : MonoBehaviour {

        [HarmonyPatch]
        private static class Patches {
            [HarmonyPatch(typeof(RundownManager), nameof(RundownManager.EndGameSession))]
            [HarmonyPrefix]
            private static void EndGameSession() {
                APILogger.Debug($"Level ended!");

                instances.Clear();
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

                foreach (CarryItem item in instances.Values) {
                    item.Footstep();
                }
            }
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

            rb.drag = 0.1f;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private Vector3 physicsPosition;
        private Quaternion physicsRotation;
        private bool prevKinematic = true;
        private PlayerAgent? carrier = null;
        private void Prefix_OnStatusChange(ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
            if (rb == null) return;

            if (placement.droppedOnFloor == false && placement.linkedToMachine == false) {
                carrier = player;
            } else {
                carrier = null;
            }

            bool physicsEnded = prevKinematic == false && rb.isKinematic == true;

            if (isRecall || status != ePickupItemStatus.PlacedInLevel) {
                rb.isKinematic = true;
                return;
            }

            if (rb.isKinematic && !physicsEnded) {
                if (player.GlobalID != PlayerManager.GetLocalPlayerAgent().GlobalID) {
                    rb.isKinematic = true;
                    return;
                }

                if (performSlip) {
                    performSlip = false;

                    if (placement.node.TryGet(out AIG_CourseNode node)) {
                        Slip(player, placement.position + Vector3.up * 1.5f, placement.rotation, node);
                    } else {
                        rb.isKinematic = true;
                        return;
                    }
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
                core.m_interact.SetActive(active: false);

                transform.position = physicsPosition;
                transform.rotation = physicsRotation;
            } else {
                savedActiveState = core.m_interact.IsActive;
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
        private float distanceSqrd = 0;
        private void Footstep() {
            if (sync == null || carrier == null) return;

            if (sync.m_stateReplicator.State.placement.droppedOnFloor == false) {
                PlayerAgent player = PlayerManager.GetLocalPlayerAgent();
                if (carrier.GlobalID == player.GlobalID && player.Inventory != null) {
                    if (player.Inventory.WieldedSlot == InventorySlot.InLevelCarry) {
                        if (startTracking == false) {
                            startTracking = true;

                            prevPosition = player.Position;
                        }

                        distanceSqrd += (player.Position - prevPosition).sqrMagnitude;
                        prevPosition = player.Position;

                        if (distanceSqrd > 1) {
                            distanceSqrd = 0;

                            performSlip = true;
                            PlayerBackpackManager.WantToDropItem_Local(player.Inventory.WieldedItem.Get_pItemData(), player.Position, player.Rotation);
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

            prevTime = Clock.Time;
            stillTimer = 0;
            startTracking = false;

            Vector3 direction = UnityEngine.Random.insideUnitSphere;
            rb.velocity = Vector3.zero;
            rb.AddForce(direction * 400.0f);

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

        private float prevTime = 0;
        private float stillTimer = 0;
        private bool savedActiveState = true;
        private bool clean = false;
        private void FixedUpdate() {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            if (!clean) {
                if (core.ItemCuller != null && core.ItemCuller.CullBucket != null) {
                    core.ItemCuller.CullBucket.CleanupLists();
                    clean = true;
                }
            }

            if (rb.isKinematic == true) {
                if (core.m_interact.IsActive != savedActiveState) core.m_interact.SetActive(active: savedActiveState);
                return;
            } else {
                savedActiveState = core.m_interact.IsActive;
            }

            if (core.m_interact.IsActive) core.m_interact.SetActive(active: false);

            AIG_CourseNode? node = GetNode(transform.position);

            const float minimumVelocity = 0.5f;
            if (rb.velocity.sqrMagnitude < minimumVelocity * minimumVelocity) {
                stillTimer += Clock.Time - prevTime;
            } else {
                stillTimer = 0;
            }

            if (node != null) {
                gameObject.transform.SetParent(node.gameObject.transform);
                core.m_itemCuller.MoveToNode(node.m_cullNode, transform.position);
            }

            pItemData_Custom custom = new pItemData_Custom();
            custom.byteState = (byte)eCarryItemCustomState.Inserted_Visible_NotInteractable; // Make it non-interactable

            if (stillTimer > 1.5f) {
                rb.velocity = Vector3.zero;
                rb.isKinematic = true;
                custom.byteState = (byte)eCarryItemCustomState.Default; // Make it interactable after stopping
            }

            sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, PlayerManager.GetLocalPlayerAgent().Owner, position: transform.position, rotation: transform.rotation, node: node, droppedOnFloor: true, forceUpdate: true, custom: custom);

            prevTime = Clock.Time;
        }
    }
}
