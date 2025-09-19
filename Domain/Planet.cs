using System.Collections.Generic;

namespace MarketBot.Domain
{
    /// <summary>
    /// Represents a planet or celestial body in the Dual Universe game world.
    /// Contains basic identifying information and available ore resources.
    /// </summary>
    public class Planet
    {
        /// <summary>
        /// Unique identifier for the planet (construct ID from database)
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// Display name of the planet
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// List of ore resources available on this planet.
        /// Extracted from json_properties.planetProperties.ores
        /// </summary>
        public List<string> Ores { get; set; } = new List<string>();
    }
}