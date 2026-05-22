using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace lionfox_resyncmodels
{
    public class ResyncModelsMod : ModSystem
    {
        ICoreServerAPI? sapi;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            this.sapi = sapi;

            sapi.ChatCommands.Create("resyncmodels")
                .WithDescription("Force every tracking client to fully respawn every online player's entity. Workaround for the PlayerModelLib invisibility race.")
                .WithAlias("rsm")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(ResyncAll);

            sapi.ChatCommands.Create("resyncmodel")
                .WithDescription("Force every tracking client to fully respawn your own player entity.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(ResyncSelf);
        }

        TextCommandResult ResyncAll(TextCommandCallingArgs args)
        {
            if (sapi == null) return TextCommandResult.Error("Server API unavailable.");
            int playersTouched = 0;
            int packetsSent = 0;

            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                int p = DespawnForOthers(player);
                if (p > 0)
                {
                    playersTouched++;
                    packetsSent += p;
                }
            }

            string msg = $"Resync: touched {playersTouched} player(s), sent {packetsSent} despawn packet(s).";
            sapi.Logger.Notification("[resyncmodels] " + msg);
            return TextCommandResult.Success(msg);
        }

        TextCommandResult ResyncSelf(TextCommandCallingArgs args)
        {
            if (sapi == null) return TextCommandResult.Error("Server API unavailable.");
            if (args.Caller.Player is not IServerPlayer player || player.Entity == null)
                return TextCommandResult.Error("No player entity to resync.");

            int packetsSent = DespawnForOthers(player);
            string msg = $"Resync self: sent {packetsSent} despawn packet(s).";
            sapi.Logger.Notification("[resyncmodels] " + msg);
            return packetsSent > 0
                ? TextCommandResult.Success(msg)
                : TextCommandResult.Error(msg + " (No clients were tracking you, so nothing happened.)");
        }

        // Sends a despawn packet for `player.Entity` to every other connected client
        // that is currently tracking it, and removes the entity from that client's
        // TrackedEntities set. The next physics tick will see the entity as newly
        // in-range and re-emit a full-entity packet — which, because the client
        // already despawned the entity, is handled as a fresh create (rebuilding
        // the renderer and re-running PlayerSkinBehavior.Initialize).
        //
        // Returns the number of despawn packets actually sent. Per-client lines go
        // to Debug; the per-player aggregate goes to Notification.
        int DespawnForOthers(IServerPlayer player)
        {
            if (sapi == null) return 0;
            if (player.Entity == null)
            {
                sapi.Logger.Notification($"[resyncmodels] {player.PlayerName}: skipped (no entity).");
                return 0;
            }
            if (sapi.World is not ServerMain serverMain)
            {
                sapi.Logger.Notification($"[resyncmodels] {player.PlayerName}: skipped (server is not ServerMain).");
                return 0;
            }

            long entityId = player.Entity.EntityId;
            string ownUid = player.PlayerUID;
            int sent = 0;
            int totalClients = 0;
            int selfSkipped = 0;
            int notTracking = 0;

            var despawnData = new EntityDespawnData { Reason = EnumDespawnReason.OutOfRange };

            foreach (var client in serverMain.Clients.Values)
            {
                totalClients++;
                if (client.Player?.PlayerUID == ownUid)
                {
                    selfSkipped++;
                    continue;
                }
                if (!client.TrackedEntities.Contains(entityId))
                {
                    notTracking++;
                    continue;
                }

                var packet = ServerPackets.GetEntityDespawnPacket(new List<EntityDespawn>
                {
                    new EntityDespawn
                    {
                        ForClientId = client.Id,
                        EntityId = entityId,
                        DespawnData = despawnData
                    }
                });
                serverMain.SendPacket(client.Id, packet);
                client.TrackedEntities.Remove(entityId);
                sent++;
                sapi.Logger.Debug($"[resyncmodels] {player.PlayerName} -> client {client.Id} ({client.Player?.PlayerName ?? "?"}): despawn sent.");
            }

            sapi.Logger.Notification($"[resyncmodels] {player.PlayerName} (eid {entityId}): {totalClients} clients total, self-skip {selfSkipped}, not-tracking {notTracking}, despawned for {sent}.");
            return sent;
        }
    }
}
