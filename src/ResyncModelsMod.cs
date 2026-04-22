using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace lionfox_resyncmodels
{
    public class ResyncModelsMod : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.ChatCommands.Create("resyncmodels")
                .WithDescription("Force-resend every online player's entity attributes to all tracking clients. Workaround for the PlayerModelLib invisibility race.")
                .WithAlias("rsm")
                .HandleWith(args => ResyncAll(sapi, args));

            sapi.ChatCommands.Create("resyncmodel")
                .WithDescription("Force-resend your own entity attributes to all tracking clients.")
                .HandleWith(args => ResyncSelf(args));
        }

        static TextCommandResult ResyncAll(ICoreServerAPI sapi, TextCommandCallingArgs args)
        {
            int count = 0;
            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                if (player.Entity == null) continue;
                player.Entity.WatchedAttributes.MarkAllDirty();
                count++;
            }
            return TextCommandResult.Success($"Resynced {count} player{(count == 1 ? "" : "s")}.");
        }

        static TextCommandResult ResyncSelf(TextCommandCallingArgs args)
        {
            if (args.Caller.Player is not IServerPlayer player || player.Entity == null)
                return TextCommandResult.Error("No player entity to resync.");

            player.Entity.WatchedAttributes.MarkAllDirty();
            return TextCommandResult.Success("Resynced your player entity.");
        }
    }
}
