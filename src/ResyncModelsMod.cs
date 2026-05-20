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
            var detail = new List<string>();

            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                int p = DespawnForOthers(player, detail);
                if (p > 0)
                {
                    playersTouched++;
                    packetsSent += p;
                }
            }

            string msg = $"Resync: touched {playersTouched} player(s), sent {packetsSent} despawn packet(s).";
            sapi.Logger.Notification("[resyncmodels] " + msg);
            foreach (var line in detail) sapi.Logger.Notification("[resyncmodels]   " + line);
            return TextCommandResult.Success(msg);
        }

        TextCommandResult ResyncSelf(TextCommandCallingArgs args)
        {
            if (sapi == null) return TextCommandResult.Error("Server API unavailable.");
            if (args.Caller.Player is not IServerPlayer player || player.Entity == null)
                return TextCommandResult.Error("No player entity to resync.");

            var detail = new List<string>();
            int packetsSent = DespawnForOthers(player, detail);
            string msg = $"Resync self: sent {packetsSent} despawn packet(s).";
            sapi.Logger.Notification("[resyncmodels] " + msg);
            foreach (var line in detail) sapi.Logger.Notification("[resyncmodels]   " + line);
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
        // Returns the number of despawn packets actually sent (so we can tell the
        // difference between "no players online to receive" and "no other players
        // are tracking this entity").
        int DespawnForOthers(IServerPlayer player, List<string> detail)
        {
            if (sapi == null) return 0;
            if (player.Entity == null)
            {
                detail.Add($"{player.PlayerName}: skipped (no entity).");
                return 0;
            }
            if (sapi.World is not ServerMain serverMain)
            {
                detail.Add($"{player.PlayerName}: skipped (server is not ServerMain).");
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
                detail.Add($"{player.PlayerName} -> client {client.Id} ({client.Player?.PlayerName ?? "?"}): despawn sent.");
            }

            detail.Add($"{player.PlayerName} (eid {entityId}): {totalClients} clients total, self-skip {selfSkipped}, not-tracking {notTracking}, despawned for {sent}.");
            return sent;
        }
    }
}
