using AIGraph;
using API;
using ButterFingers.BepInEx;
using Gear;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;

// TODO(randomuserhi): Clean up code greatly => statically store local player etc...

namespace ButterFingers {
    [HarmonyPatch]
    internal class ResourcePack : MonoBehaviour {

        [HarmonyPatch]
        private static class Patches {
            [HarmonyPatch(typeof(ResourcePackPickup), nameof(ResourcePackPickup.Setup))]
            [HarmonyPostfix]
            private static void Setup(ResourcePackPickup __instance) {
                ResourcePack physicsPack = new GameObject().AddComponent<ResourcePack>();
                physicsPack.core = __instance;
                physicsPack.sync = __instance.GetComponent<LG_PickupItem_Sync>();
            }

            [HarmonyPatch(typeof(ResourcePackPickup), nameof(ResourcePackPickup.OnSyncStateChange))]
            [HarmonyPrefix]
            private static void Prefix_StatusChange(ResourcePackPickup __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
                int instance = __instance.GetInstanceID();
                if (instances.ContainsKey(instance)) {
                    instances[instance].Prefix_OnStatusChange(status, placement, player, isRecall);
                }
            }

            [HarmonyPatch(typeof(ResourcePackPickup), nameof(ResourcePackPickup.OnSyncStateChange))]
            [HarmonyPostfix]
            [HarmonyPriority(Priority.High)]
            private static void Postfix_StatusChange(ResourcePackPickup __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
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

                foreach (ResourcePack item in instances.Values) {
                    item.Footstep();
                }
            }
        }

        private int instance;
        private ResourcePackPickup? core;
        private LG_PickupItem_Sync? sync;
        private Rigidbody? rb;
        private CapsuleCollider? collider;

        internal static Dictionary<int, ResourcePack> instances = new Dictionary<int, ResourcePack>();

        private void Start() {
            if (sync == null || core == null) return;

            instance = core.GetInstanceID();

            instances.Add(instance, this);

            gameObject.layer = LayerManager.LAYER_DEBRIS;

            rb = gameObject.AddComponent<Rigidbody>();
            collider = gameObject.AddComponent<CapsuleCollider>();

            rb.drag = 0.01f;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            oldPosition = core.transform.position;
            oldRotation = core.transform.rotation;

            PhysicMaterial material = new PhysicMaterial();
            material.bounciness = 0.75f;
            material.bounceCombine = PhysicMaterialCombine.Maximum;
            collider.material = material;
        }

        private bool prevKinematic = true;
        private PlayerAgent? carrier = null;
        private float grace = 0;
        private void Prefix_OnStatusChange(ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
            if (rb == null) return;
            if (sync == null) return;

            if (placement.droppedOnFloor == false && placement.linkedToMachine == false) {
                carrier = player;
            } else {
                carrier = null;
            }

            bool physicsEnded = prevKinematic == false && rb.isKinematic == true;

            if (Clock.Time > grace) {
                if (isRecall || status != ePickupItemStatus.PlacedInLevel) {
                    rb.isKinematic = true;
                    return;
                }
            }

            if (rb.isKinematic && !physicsEnded) {
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
        }

        private void Postfix_OnStatusChange(ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
            if (rb == null || core == null || sync == null) return;

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

        private static ItemInLevel? GetLevelItemFromItemData(pItemData itemData) {
            if (!PlayerBackpackManager.TryGetItemInLevelFromItemData(itemData, out var item)) {
                return null;
            }
            if (item.TryCast<ItemInLevel>() == null) {
                return null;
            }
            return item.TryCast<ItemInLevel>();
        }

        private bool startTracking = false;
        private Vector3 prevPosition = Vector3.zero;
        private float distanceSqrd = 0;
        private void Footstep() {
            if (sync == null || carrier == null) return;

            if (sync.m_stateReplicator.State.placement.droppedOnFloor == false) {
                PlayerAgent player = PlayerManager.GetLocalPlayerAgent();
                if (carrier.GlobalID == player.GlobalID && player.Inventory != null) {
                    if (player.Inventory.WieldedSlot == InventorySlot.ResourcePack) {
                        if (startTracking == false) {
                            startTracking = true;

                            prevPosition = player.Position;
                        }

                        distanceSqrd += (player.Position - prevPosition).sqrMagnitude;
                        prevPosition = player.Position;

                        if (distanceSqrd > ConfigManager.DistancePerRoll * ConfigManager.DistancePerRoll) {
                            distanceSqrd = 0;

                            if (UnityEngine.Random.Range(0.0f, 1.0f) < ConfigManager.ResourceProbability) {
                                performSlip = true;
                                APILogger.Debug("slip");

                                PlayerInventoryBase inventory = player.Inventory;
                                InventorySlot? inventorySlot = inventory != null ? inventory.WieldedSlot : null;
                                if (!inventorySlot.HasValue || (int)(inventorySlot.GetValueOrDefault() - 4) > 1) {
                                    return;
                                }
                                ItemEquippable wieldedItem = player.Inventory.WieldedItem;
                                if (wieldedItem == null) {
                                    return;
                                }
                                ItemInLevel? levelItemFromItemData = GetLevelItemFromItemData(wieldedItem.Get_pItemData());
                                if (levelItemFromItemData == null) {
                                    return;
                                }
                                iPickupItemSync syncComponent = levelItemFromItemData.GetSyncComponent();
                                if (syncComponent != null) {
                                    InventorySlot slot = levelItemFromItemData.Get_pItemData().slot;
                                    InventorySlotAmmo inventorySlotAmmo = PlayerBackpackManager.GetLocalOrSyncBackpack().AmmoStorage.GetInventorySlotAmmo(slot);
                                    pItemData_Custom custom = wieldedItem.GetCustomData();
                                    custom.ammo = inventorySlotAmmo.AmmoInPack;
                                    syncComponent.AttemptPickupInteraction(ePickupItemInteractionType.Place, SNet.LocalPlayer, position: player.transform.position, rotation: player.transform.rotation, node: player.CourseNode, droppedOnFloor: true, forceUpdate: true, custom: custom);
                                }
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

            rb.isKinematic = false;

            prevTime = Clock.Time;
            stillTimer = 0;
            grace = 0;
            timer = 0;
            visible = true;
            startTracking = false;
            grace = Clock.Time + 0.4f;

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

        private static Vector3 oob = new Vector3(0, -2000f, -2000f);
        private Vector3 oldPosition = Vector3.zero;
        private Quaternion oldRotation = Quaternion.identity;

        private float prevTime = 0;
        private float stillTimer = 0;
        private float timer = 0;
        private bool visible = false;
        private void FixedUpdate() {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            if (rb.isKinematic == true) {
                return;
            }

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

            // Additional condition after stillTimer to stop simulating physics if cell is out of bounds
            // TODO(randomuserhi): Check falling below other dimension boundaries instead of just -2500
            if (stillTimer > 1.5f || transform.position.y < -2500) {
                LocalPlayerAgent player = PlayerManager.GetLocalPlayerAgent().Cast<LocalPlayerAgent>();
                PlayerPingTarget playerPingTarget = gameObject.AddComponent<PlayerPingTarget>();
                playerPingTarget.m_pingTargetStyle = eNavMarkerStyle.PlayerPingCarryItem; // TODO
                player.m_pingTarget = gameObject.GetComponent<iPlayerPingTarget>();
                player.m_pingPos = transform.position;
                if (player.m_pingTarget != null) {
                    player.TriggerMarkerPing(player.m_pingTarget, gameObject, player.m_pingPos);
                }
                UnityEngine.Object.Destroy(playerPingTarget);

                timer = 0;
                visible = true;
                rb.velocity = Vector3.zero;
                rb.isKinematic = true;
            }

            if (visible) {
                if (Clock.Time > timer) {
                    visible = !visible;
                    timer = Clock.Time + 0.05f;
                }

                oldPosition = transform.position;
                oldRotation = transform.rotation;
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, PlayerManager.GetLocalPlayerAgent().Owner, position: transform.position, rotation: transform.rotation, node: node, droppedOnFloor: true, forceUpdate: true, custom: sync.m_stateReplicator.State.custom);
            } else {
                if (Clock.Time > timer) {
                    visible = !visible;
                    timer = Clock.Time + 0.4f;
                }
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, PlayerManager.GetLocalPlayerAgent().Owner, position: oob, rotation: transform.rotation, node: node, droppedOnFloor: true, forceUpdate: true, custom: sync.m_stateReplicator.State.custom);
            }


            prevTime = Clock.Time;

        }

        private void OnApplicationQuit() {
            ForceStop();
        }

        internal void ForceStop() {
            APILogger.Debug("FORCE STOP RESOURCEPACK");

            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;
            if (rb.isKinematic == true) return;

            rb.isKinematic = true;

            AIG_CourseNode? node = PlayerManager.GetLocalPlayerAgent().GoodNodeCluster.m_courseNode;
            node.m_nodeCluster.TryGetClosetPositionInCluster(transform.position, out var closestPosition);
            sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, PlayerManager.GetLocalPlayerAgent().Owner, position: closestPosition, rotation: oldRotation, node: core.m_courseNode, droppedOnFloor: true, forceUpdate: true, custom: sync.m_stateReplicator.State.custom);
        }
    }
}
