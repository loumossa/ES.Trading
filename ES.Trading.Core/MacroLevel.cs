namespace ES.Trading.Core.Models
{
    public class MacroLevel
    {
        public int Id { get; set; }

        /// <summary>
        /// ISO 8601 date (yyyy-MM-dd) of the session this level is relevant for.
        /// Entered the night before in the Desktop App.
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>E.g. "Q2 Open", "Prior Day High", "Yearly Open", "Prior Week Low"</summary>
        public string Label { get; set; } = string.Empty;

        public double Price { get; set; }
    }
}
