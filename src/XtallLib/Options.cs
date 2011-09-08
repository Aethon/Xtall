using System;
using System.Collections.Generic;

namespace Xtall
{
    public class Options
    {
        public IDictionary<string, string> Keyed { get; private set; }
        public IList<string> Loose { get; private set; }

        public Options(IList<string> args)
        {
            Keyed = new Dictionary<string, string>();
            Loose = new List<string>();

            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("/") || arg.StartsWith("-"))
                {
                    var key = arg.Substring(1);
                    string value = null;
                    if (arg.EndsWith(":"))
                    {
                        if (i == args.Count - 1)
                            throw new ArgumentException("Argument '{0}' did not provide a value", key);
                        value = args[i + 1];
                        i++;
                    }
                    Keyed[key] = value;
                }
                else
                {
                    Loose.Add(arg);
                }
            }
        }
    }
}