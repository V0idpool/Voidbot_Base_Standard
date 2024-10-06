using Discord;
namespace Voidbot_Discord_Bot_GUI
{
    internal class EmbedVoid
    {
        private MainProgram _botsinstance = new MainProgram();
        public EmbedBuilder EmbedBasicMsg(Discord.Color color, string title, string titleUrl, string description)
        {
            // Inside a command, event listener, etc.
            EmbedBuilder exampleEmbed = new EmbedBuilder()
                .WithColor(new Discord.Color(color))
                .WithTitle(title)
                .WithUrl(titleUrl)
                .WithAuthor(author =>
                {
                    author
                        .WithName("VoidBot")
                        .WithIconUrl("https://cdn.discordapp.com/app-icons/1199181623674023938/0865c9879f7178b790cac1f71c2bd007.png")
                        .WithUrl("https://github.com/V0idpool/VoidBot-Discord-Bot-GUI/releases");
                })
                .WithDescription(description)
                .WithTimestamp(DateTimeOffset.UtcNow);//true or fale to show

            return exampleEmbed;
        }
        //public void Embedder(Discord.Color color, string title, string titleURL, string embedimage, string authorname, string authoricon, string authorURL, string description, string thumbURL, string regfield, string regfieldvalue, string timestamp, string footertext, string footerimage)
        //{
        //    // Inside a command, event listener, etc.
        //    EmbedBuilder exampleEmbed = new EmbedBuilder()
        //        .WithColor(new Discord.Color(color))
        //        .WithTitle(title)
        //        .WithUrl(titleURL)
        //        .WithAuthor(author =>
        //        {
        //            author
        //                .WithName(authorname)
        //                .WithIconUrl(authoricon)
        //                .WithUrl(authorURL);
        //        })
        //        .WithDescription(description)
        //        .WithThumbnailUrl(thumbURL)
        //        .AddField(regfield, regfieldvalue)
        //        .AddField("\u200B", "\u200B")
        //        .AddField("Inline field title", "Some value here", true)
        //        .AddField("Inline field title", "Some value here", true)
        //        .AddField("Inline field title", "Some value here", true)
        //        .WithImageUrl(embedimage)
        //        .WithTimestamp(DateTimeOffset.UtcNow)//true or fale to show
        //        .WithFooter(footer =>
        //        {
        //            footer
        //                .WithText(footertext)
        //                .WithIconUrl(footerimage);
        //        });

        //    //  await botInstance.SendMessageToDiscord(embed: exampleEmbed.Build());
        //}

    }
}
