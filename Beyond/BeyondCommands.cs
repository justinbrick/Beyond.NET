using Discord;
using Discord.Interactions;

namespace Beyond
{

    public class BeyondCommandService
    {

        public BeyondCommandService()
        {

        }
    }

    public class BeyondCommands : InteractionModuleBase
    {
        private readonly BeyondCommandService _service;
        public BeyondCommands(BeyondCommandService s)
        {
            _service = s;
        }

        [SlashCommand("vote", "Vote for Gumby of the Month")]
        public async Task Vote(
            [Summary(description:"candidate")] IUser candidate
            )
        {
            var user = Context.Interaction.User;
            // If the user is voting for themselves, or they are voting for a bot, then tell them off.
            if (user.Id == candidate.Id || candidate.IsBot)
            {
                await RespondAsync("You cannot vote for that person!");
                return;
            }

            await RespondAsync($"Voted for {candidate.Username}");
        }
    }
}
