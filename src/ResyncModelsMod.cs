using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace lionfox_resyncmodels
{
    public class ResyncModelsMod : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.ChatCommands.Create("resyncmodels")
                .WithDescription("Force every tracking client to fully respawn every online player's entity. Workaround for the PlayerModelLib invisibility race.")
                .WithAlias("rsm")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(args => ResyncAll(sapi, args));

            sapi.ChatCommands.Create("resyncmodel")
                .WithDescription("Force every tracking client to fully respawn your own player entity.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args => ResyncSelf(sapi, args));
        }

        static TextCommandResult ResyncAll(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            int count = 0;
            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                if (UntrackForAll(sapi, player)) count++;
            }
            return TextCommandResult.Success($"Resynced {count} player{(count == 1 ? "" : "s")}.");
        }

        static TextCommandResult ResyncSelf(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            if (args.Caller.Player is not IServerPlayer player || player.Entity == null)
                return TextCommandResult.Error("No player entity to resync.");

            return UntrackForAll(sapi, player)
                ? TextCommandResult.Success("Resynced your player entity.")
                : TextCommandResult.Error("Could not resync — no entity or server state unavailable.");
        }

        // Removes the entity from every connected client's TrackedEntities set, so the
        // server's next physics tick treats them as newly-in-range and emits a fresh
        // full-spawn packet — forcing the client (and PlayerModelLib) to rebuild the renderer.
        static bool UntrackForAll(ICoreServerAPI sapi, IServerPlayer player)
        {
            if (player.Entity == null) return false;
            if (sapi.World is not ServerMain serverMain) return false;

            long entityId = player.Entity.EntityId;
            foreach (var client in serverMain.Clients.Values)
            {
                client.TrackedEntities.Remove(entityId);
            }
            return true;
        }
    }
}
