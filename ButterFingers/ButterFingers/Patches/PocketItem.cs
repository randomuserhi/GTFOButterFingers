﻿using AIGraph;
using API;
using ButterFingers.BepInEx;
using HarmonyLib;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;

namespace ButterFingers {
    [HarmonyPatch]
    internal class PocketItem : MonoBehaviour {

        [HarmonyPatch]
        private static class Patches {
            [HarmonyPatch(typeof(GenericSmallPickupItem_Core), nameof(GenericSmallPickupItem_Core.Setup))]
            [HarmonyPostfix]
            private static void Setup(GenericSmallPickupItem_Core __instance) {
                PocketItem physicsPack = new GameObject().AddComponent<PocketItem>();
                physicsPack.core = __instance;
                physicsPack.sync = __instance.GetComponent<LG_PickupItem_Sync>();
            }

            [HarmonyPatch(typeof(GenericSmallPickupItem_Core), nameof(GenericSmallPickupItem_Core.OnSyncStateChange))]
            [HarmonyPrefix]
            private static void Prefix_StatusChange(GenericSmallPickupItem_Core __instance, ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
                int instance = __instance.GetInstanceID();
                if (instances.ContainsKey(instance)) {
                    instances[instance].Prefix_OnStatusChange(status, placement, player, isRecall);
                }
            }

            private static bool startTracking = false;
            private static Vector3 prevPosition = Vector3.zero;
            private static float distance = 0;
            private static List<uint> groups = new List<uint>();

            [HarmonyPatch(typeof(PlayerAgent), nameof(PlayerAgent.Update))]
            [HarmonyPrefix]
            private static void Update(PlayerAgent __instance) {
                if (!__instance.Owner.IsLocal || __instance.Owner.IsBot) return;
                if (Clock.Time < Cooldown.timer) return;

                if (PlayerBackpackManager.TryGetBackpack(__instance.Owner, out PlayerBackpack backpack)) {
                    if (backpack.ItemIDToPocketItemGroup.Count == 0) return;

                    if (startTracking == false) {
                        startTracking = true;

                        prevPosition = __instance.Position;
                    }

                    distance += (__instance.Position - prevPosition).magnitude;
                    prevPosition = __instance.Position;

                    if (distance < ConfigManager.DistancePerRoll) return;

                    distance = 0;

                    if (UnityEngine.Random.Range(0.0f, 1.0f) > ConfigManager.ItemInPocketProbability) return;

                    foreach (uint group in backpack.ItemIDToPocketItemGroup.keys) {
                        groups.Add(group);
                    }
                    Player.PocketItem pitem = backpack.ItemIDToPocketItemGroup[groups[UnityEngine.Random.Range(0, backpack.ItemIDToPocketItemGroup.Count)]][0];
                    groups.Clear();

                    pItemData fillerData = new pItemData();
                    fillerData.itemID_gearCRC = pitem.itemID;
                    fillerData.slot = InventorySlot.InPocket;
                    fillerData.replicatorRef = pitem.replicatorRef;

                    if (PlayerBackpackManager.TryGetItemInLevelFromItemData(fillerData, out Item item)) {
                        ItemInLevel? levelItemFromItemData = item.TryCast<ItemInLevel>();
                        if (levelItemFromItemData == null) {
                            APILogger.Debug("Death - could not find pocket item.");
                            return;
                        }

                        int instance = levelItemFromItemData.GetInstanceID();
                        if (instances.ContainsKey(instance)) {
                            instances[instance].TriggerSlip();
                        } else {
                            APILogger.Debug($"Death - could not find pocket item instance {instance}");
                        }
                    }
                }
            }

            private static List<Player.PocketItem> pitems = new List<Player.PocketItem>();

            [HarmonyPatch(typeof(PLOC_Downed), nameof(PLOC_Downed.CommonEnter))]
            [HarmonyPrefix]
            [HarmonyWrapSafe]
            private static void Prefix_CommonEnter(PLOC_Downed __instance) {
                if (!__instance.m_owner.Owner.IsLocal || __instance.m_owner.Owner.IsBot) return;

                PlayerAgent player = PlayerManager.GetLocalPlayerAgent();
                if (PlayerBackpackManager.TryGetBackpack(player.Owner, out PlayerBackpack backpack)) {
                    foreach (var kvp in backpack.ItemIDToPocketItemGroup) {
                        for (int i = 0; i < kvp.Value.Count; ++i) {
                            pitems.Add(kvp.Value[i]);
                        }
                    }

                    APILogger.Debug($"Dropping {pitems.Count} pocket items.");

                    foreach (Player.PocketItem pitem in pitems) {
                        pItemData fillerData = new pItemData();
                        fillerData.itemID_gearCRC = pitem.itemID;
                        fillerData.slot = InventorySlot.InPocket;
                        fillerData.replicatorRef = pitem.replicatorRef;

                        if (PlayerBackpackManager.TryGetItemInLevelFromItemData(fillerData, out Item item)) {
                            ItemInLevel? levelItemFromItemData = item.TryCast<ItemInLevel>();
                            if (levelItemFromItemData == null) {
                                APILogger.Debug("Death - could not find pocket item.");
                                return;
                            }

                            int instance = levelItemFromItemData.GetInstanceID();
                            if (instances.ContainsKey(instance)) {
                                instances[instance].TriggerSlip();
                            } else {
                                APILogger.Debug($"Death - could not find pocket item instance {instance}");
                            }
                        }
                    }

                    pitems.Clear();
                }
            }
        }

        private int instance;
        private GenericSmallPickupItem_Core? core;
        private LG_PickupItem_Sync? sync;
        private Rigidbody? rb;
        private CapsuleCollider? collider;

        internal static Dictionary<int, PocketItem> instances = new Dictionary<int, PocketItem>();

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
            material.bounciness = 0.4f;
            material.bounceCombine = PhysicMaterialCombine.Maximum;
            collider.material = material;
        }

        private PlayerAgent? carrier = null;
        private void Prefix_OnStatusChange(ePickupItemStatus status, pPickupPlacement placement, PlayerAgent player, bool isRecall) {
            if (rb == null) return;
            if (player == null) return;
            if (sync == null) return;

            if (status == ePickupItemStatus.PickedUp && placement.linkedToMachine == false) {
                carrier = player;
            } else {
                carrier = null;
            }

            if (isRecall) {
                rb.isKinematic = true;
                return;
            }

            APILogger.Debug($"{instance} {rb.isKinematic} {performSlip}");
            if (rb.isKinematic && performSlip) {
                performSlip = false;

                Slip(player, placement.position + Vector3.up * 1.5f, placement.rotation, player.m_courseNode);
            }
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

        private void TriggerSlip() {
            if (sync == null || carrier == null) return;

            performSlip = true;
            APILogger.Debug($"{instance} slip");
            pItemData_Custom custom = sync.m_stateReplicator.m_currentState.custom;
            sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, SNet.LocalPlayer, position: carrier.transform.position, rotation: carrier.transform.rotation, node: carrier.CourseNode, droppedOnFloor: false, forceUpdate: true, custom: custom);
        }

        private bool performSlip = false;
        private void Slip(PlayerAgent player, Vector3 position, Quaternion rotation, AIG_CourseNode node) {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            rb.isKinematic = false;

            prevTime = Clock.Time;
            stillTimer = 0;
            timer = 0;
            visible = true;
            pingTimer = 0;
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

        private static Vector3 oob = new Vector3(0, -2000f, -2000f);
        private Vector3 oldPosition = Vector3.zero;
        private Quaternion oldRotation = Quaternion.identity;

        private float prevTime = 0;
        private float stillTimer = 0;
        private float timer = 0;
        private bool visible = false;
        private float pingTimer = 0;
        private void FixedUpdate() {
            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;

            if (rb.isKinematic == true) {
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

            // Additional condition after stillTimer to stop simulating physics if cell is out of bounds
            // TODO(randomuserhi): Check falling below other dimension boundaries instead of just -2500
            if (stillTimer > 1f || transform.position.y < -2500) {
                timer = 0;
                pingTimer = 0;
                visible = true;
                rb.velocity = Vector3.zero;
                rb.isKinematic = true;
            }

            if (visible) {
                if (Clock.Time > timer) {
                    visible = !visible;
                    timer = Clock.Time + 0.02f;
                }

                if (Clock.Time > pingTimer) {
                    pingTimer = Clock.Time + 1.5f;
                    LocalPlayerAgent player = PlayerManager.GetLocalPlayerAgent().Cast<LocalPlayerAgent>();
                    player.m_pingTarget = core.GetComponent<iPlayerPingTarget>();
                    player.m_pingPos = transform.position;
                    if (player.m_pingTarget != null) {
                        GuiManager.AttemptSetPlayerPingStatus(player, true, player.m_pingPos, player.m_pingTarget.PingTargetStyle);
                    } else {
                        GuiManager.AttemptSetPlayerPingStatus(player, true, player.m_pingPos, eNavMarkerStyle.PlayerPingPickupObjectiveItem);
                    }
                }

                oldPosition = transform.position;
                oldRotation = transform.rotation;
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, null, position: transform.position, rotation: transform.rotation, node: node, droppedOnFloor: false, forceUpdate: true, custom: sync.m_stateReplicator.State.custom);
            } else {
                if (Clock.Time > timer) {
                    visible = !visible;
                    timer = Clock.Time + 0.4f;
                }
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, null, position: oob, rotation: transform.rotation, node: node, droppedOnFloor: false, forceUpdate: true, custom: sync.m_stateReplicator.State.custom);
            }


            prevTime = Clock.Time;

        }

        private void OnApplicationQuit() {
            ForceStop();
        }

        internal void ForceStop() {
            APILogger.Debug("FORCE STOP POCKET ITEM");

            if (core == null || sync == null) return;
            if (rb == null || collider == null) return;
            if (rb.isKinematic == true) return;

            rb.isKinematic = true;

            AIG_CourseNode? node = PlayerManager.GetLocalPlayerAgent().GoodNodeCluster.m_courseNode;
            node.m_nodeCluster.TryGetClosetPositionInCluster(transform.position, out var closestPosition);
            sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, null, position: closestPosition, rotation: oldRotation, node: core.m_courseNode, droppedOnFloor: false, forceUpdate: true, custom: sync.m_stateReplicator.State.custom);
        }
    }
}
