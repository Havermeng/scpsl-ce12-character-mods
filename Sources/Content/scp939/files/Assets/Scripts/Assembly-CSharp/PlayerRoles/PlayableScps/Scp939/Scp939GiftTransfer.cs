using System;
using System.Collections.Generic;
using InventorySystem;
using InventorySystem.Items;
using Mirror;
using UnityEngine;

namespace PlayerRoles.PlayableScps.Scp939
{
    public struct Scp939GiftAnimationMessage : NetworkMessage
    {
        public uint Sequence, Giver, Receiver;
        public ushort Serial;
        public ItemType Type;
        public bool Accepted;
        public double StartTime;
        // 0 begin, 1 authoritative ownership change, 2 cancel, 3 completed.
        public byte Stage;
        public string Error;
    }

    public static class Scp939GiftTransfer
    {
        private sealed class Transfer
        {
            public ReferenceHub Giver, Receiver;
            public PlayerRoleBase GiverRole, ReceiverRole;
            public ItemBase Item;
            public ushort PreviousSelection;
            public Vector3 GiverStart, ReceiverStart;
            public Scp939GiftAnimationMessage Message;
            public bool FromInventory, Committed;
            public Action Reaction;
        }
        private static readonly List<Transfer> Active = new List<Transfer>();
        private static readonly Dictionary<uint, Scp939GiftPresentation> Clients = new Dictionary<uint, Scp939GiftPresentation>();
        private static uint _sequence;

        public static void Register()
        {
            foreach (var p in Clients.Values) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            Clients.Clear(); Active.Clear();
            NetworkClient.ReplaceHandler<Scp939GiftAnimationMessage>(ClientApply);
        }

        public static bool Busy(ReferenceHub hub)
        {
            foreach (var t in Active) if (t.Giver == hub || t.Receiver == hub) return true;
            return false;
        }

        public static void InterruptSettledPresentation(ReferenceHub receiver)
        {
            if (receiver == null) return;
            foreach (var p in Clients.Values)
                if (p != null && p.Settled && p.ReceiverHub == receiver)
                    p.Cancel();
        }

        public static bool TryStart(ReferenceHub receiver, ReferenceHub giver, ushort serial, ItemType type,
                                    bool fromInventory, Action reaction, out string error)
        {
            error = null;
            if (Busy(giver) || Busy(receiver)) { error = "Сначала закончите передачу текущего подарка."; return false; }
            var gm = Scp939GiftPresentation.ModelOf(giver); var rm = Scp939GiftPresentation.ModelOf(receiver);
            if (gm == null || rm == null) { error = "Для вручения нужны две действующие модели персонажей."; return false; }
            var ga = new Scp939GiftArm(gm.GetComponentInChildren<Animator>(true), false);
            var ra = new Scp939GiftArm(rm.GetComponentInChildren<Animator>(true), true);
            if (!ga.Valid || !ra.Valid) { error = "У этого персонажа нет подходящей руки для вручения."; return false; }
            float reach = Mathf.Min(ga.Reach, ra.Reach);
            Vector3 contact = Vector3.Lerp(ga.Upper.position, ra.Upper.position, .5f) - Vector3.up * (reach * .33f);
            if (Vector3.Distance(ga.Upper.position, contact) > ga.Reach * .90f ||
                Vector3.Distance(ra.Upper.position, contact) > ra.Reach * .90f)
            { error = "Подойдите ближе: подарок должен быть в пределах досягаемости обеих рук."; return false; }
            Vector3 direction = receiver.transform.position - giver.transform.position; direction.y = 0f;
            Vector3 look = giver.PlayerCameraReference.forward; look.y = 0f;
            if (direction.sqrMagnitude < .25f || Vector3.Angle(look, direction) > 65f || !Facing(receiver, giver))
            { error = "Встаньте напротив SCP-939 и повернитесь к ней."; return false; }
            ItemBase item = null;
            if (fromInventory && (!giver.inventory.UserInventory.Items.TryGetValue(serial, out item) || item == null || item.ItemTypeId != type))
            { error = "Выбранного подарка больше нет в инвентаре."; return false; }
            if (!InventoryItemLoader.AvailableItems.TryGetValue(type, out var template) || template.PickupDropModel == null)
            { error = "У этого предмета нет модели для передачи."; return false; }
            if (giver.inventory.CurInstance != null && !giver.inventory.CurInstance.AllowHolster)
            { error = "Сначала завершите действие с предметом в руках."; return false; }
            var transfer = new Transfer
            {
                Giver = giver, Receiver = receiver, GiverRole = giver.roleManager.CurrentRole,
                ReceiverRole = receiver.roleManager.CurrentRole, Item = item, FromInventory = fromInventory,
                PreviousSelection = giver.inventory.CurItem.SerialNumber, GiverStart = giver.transform.position,
                ReceiverStart = receiver.transform.position, Reaction = reaction,
                Message = new Scp939GiftAnimationMessage
                {
                    Sequence = ++_sequence, Giver = giver.netId, Receiver = receiver.netId,
                    Serial = serial, Type = type, Accepted = Scp939Gifts.Accepts(type),
                    StartTime = NetworkTime.time + .45, Stage = 0
                }
            };
            // Holstering leaves the real item owned by the giver until the contact event.
            giver.inventory.ServerSelectItem(0);
            Active.Add(transfer); Send(transfer, 0);
            return true;
        }

        public static void Tick()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var t = Active[i];
                bool sameRoles = t.Giver != null && t.Receiver != null &&
                    t.Giver.roleManager.CurrentRole == t.GiverRole && t.Receiver.roleManager.CurrentRole == t.ReceiverRole &&
                    Scp939Interactions.IsInteractable(t.Receiver);
                bool withinReach = sameRoles && Vector3.Distance(t.Giver.transform.position, t.GiverStart) < .25f &&
                    Vector3.Distance(t.Receiver.transform.position, t.ReceiverStart) < .25f &&
                    Facing(t.Giver, t.Receiver) && Facing(t.Receiver, t.Giver);
                bool hasItem = !t.FromInventory || t.Committed || t.Giver != null && t.Giver.inventory != null &&
                    t.Giver.inventory.UserInventory.Items.TryGetValue(t.Message.Serial, out var item) && item == t.Item;
                bool handsFree = t.Giver != null && t.Giver.inventory != null && t.Giver.inventory.CurItem.TypeId == ItemType.None;
                if (!sameRoles || !withinReach || !hasItem || !handsFree)
                {
                    Send(t, 2); RestoreSelection(t);
                    Active.RemoveAt(i);
                    if (t.Committed) t.Reaction?.Invoke();
                    continue;
                }
                double time = NetworkTime.time - t.Message.StartTime;
                if (!t.Committed && time >= Scp939GiftPresentation.TransferAt)
                {
                    if (t.Message.Accepted && t.FromInventory) t.Giver.inventory.ServerRemoveItem(t.Message.Serial, null);
                    t.Committed = true; Send(t, 1);
                }
                if (time < Scp939GiftPresentation.CompleteAt) continue;
                Active.RemoveAt(i); Send(t, 3); RestoreSelection(t);
                t.Reaction?.Invoke();
            }
        }

        private static bool Facing(ReferenceHub actor, ReferenceHub other)
        {
            if (actor == null || other == null || actor.PlayerCameraReference == null) return false;
            Vector3 direction = other.transform.position - actor.transform.position; direction.y = 0f;
            Vector3 look = actor.PlayerCameraReference.forward; look.y = 0f;
            return direction.sqrMagnitude > .01f && look.sqrMagnitude > .01f && Vector3.Angle(look, direction) <= 65f;
        }

        private static void RestoreSelection(Transfer t)
        {
            if (t.Giver == null || t.Giver.roleManager.CurrentRole != t.GiverRole || t.Giver.inventory == null ||
                t.Giver.inventory.CurItem.TypeId != ItemType.None || t.PreviousSelection == 0 ||
                !t.Giver.inventory.UserInventory.Items.ContainsKey(t.PreviousSelection)) return;
            t.Giver.inventory.ServerSelectItem(t.PreviousSelection);
        }

        private static void Send(Transfer t, byte stage)
        {
            var message = t.Message; message.Stage = stage;
            NetworkServer.SendToReady(message);
        }

        private static void ClientApply(Scp939GiftAnimationMessage msg)
        {
            if (msg.Stage == 4)
            {
                Scp939ActionMenu.Open("Подарок", msg.Error,
                    new[] { new Scp939ActionMenu.Entry("Понятно", Scp939ActionMenu.Close) });
                return;
            }
            if (msg.Stage == 0)
            {
                if (Clients.ContainsKey(msg.Sequence)) return;
                if (!ReferenceHub.TryGetHubNetID(msg.Giver, out var giver) || !ReferenceHub.TryGetHubNetID(msg.Receiver, out var receiver)) return;
                var old = new List<uint>();
                foreach (var pair in Clients)
                    if (pair.Value == null || pair.Value.ReceiverHub == receiver || pair.Value.GiverHub == giver)
                    { if (pair.Value != null) pair.Value.RetireForReplacement(); old.Add(pair.Key); }
                foreach (uint key in old) Clients.Remove(key);
                var presentation = Scp939GiftPresentation.StartFor(receiver, giver, msg);
                if (presentation != null) Clients.Add(msg.Sequence, presentation);
                else Debug.LogWarning("[939 gift] Presentation unavailable for " + msg.Type + "; check the character/item rig.");
                return;
            }
            if (!Clients.TryGetValue(msg.Sequence, out var p) || p == null) return;
            if (msg.Stage == 1) p.ConfirmTransfer();
            else if (msg.Stage == 3) p.ConfirmFinish();
            else if (msg.Stage == 2) { p.Cancel(); Clients.Remove(msg.Sequence); }
        }
    }
}
