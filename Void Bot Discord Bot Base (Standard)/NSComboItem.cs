namespace Voidbot_Discord_Bot_GUI
{
    // Helper for the terrible nsComboBoxes
    internal class NSComboItem
    {
        private string name;

        public NSComboItem(string name)
        {
            this.name = name;
        }

        public ulong Tag { get; set; }
    }
}