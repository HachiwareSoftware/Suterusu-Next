namespace Suterusu.Bootstrap
{
    public class StartupOptions
    {
        public bool DebugEnabled { get; private set; }
        public bool OpenSettings { get; private set; }

        private StartupOptions() { }

        public static StartupOptions Parse(string[] args)
        {
            var opts = new StartupOptions();

            if (args == null)
                return opts;

            foreach (string arg in args)
            {
                string normalized = arg.Trim().ToLowerInvariant();
                if (normalized == "--debug" || normalized == "-debug" || normalized == "/debug")
                    opts.DebugEnabled = true;
                else if (normalized == "--open-settings" || normalized == "-open-settings" || normalized == "/open-settings")
                    opts.OpenSettings = true;
            }

            return opts;
        }
    }
}
