//-----------------------------------------------------------------------
// EVE App Config
//-----------------------------------------------------------------------

namespace HISA.EVEData
{
    public class EveAppConfig
    {
        #region Fields

        /// <summary>
        /// Callback URL for eve
        /// </summary>
        public const string CallbackURL = @"http://localhost:8762/callback/";

        /// <summary>
        /// Client ID from the EVE Developer setup
        /// </summary>
        public const string ClientID = "ID Goes here.. ";

        /// <summary>
        /// HISA Version Tagline
        /// </summary>
        public const string HISA_TITLE = "Haakario Interstellar Survey Authority";

        /// <summary>
        /// HISA Version
        /// </summary>
        public const string HISA_VERSION = "HISA";

        /// <summary>
        /// Folder to store all of the data from
        /// </summary>
        public static readonly string StorageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HISA");

        /// <summary>
        /// Folder to store all of the data from
        /// </summary>
        public static readonly string VersionStorage = StorageRoot;

        #endregion Fields
    }
}

