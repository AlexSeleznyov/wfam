using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using NLog;

namespace Seleznyov.Com.WhichFilesAreMissing
{
    class Params
    {
        public string BasePath;
        public string UnsortedPath;
        public string ExtList;
        public string OutputMissingFileName;
        public string OutputDifferenceFileName;
    }

    class Program
    {
        private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

        const string PARAM_BASE = "-base";
        const string PARAM_UNSORTED = "-unsorted";
        const string PARAM_EXT = "-ext";
        const string PARAM_MISS = "-miss";
        const string PARAM_DIFF = "-diff";

        static Program()
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        static void Main(string[] args)
        {
            s_logger.Info("WhichFilesAreMissing console utility");
            if (args.Length == 0)
            {
                s_logger.Error("No parameters provided. Use -h for help.");
            }
            else
            {
                var sw = args[0];
                switch (sw)
                {
                    case "-h":
                        ShowHelp();
                        return;
                }

                var p = ParseParams(args);

                if (!string.IsNullOrEmpty(p.BasePath) && !string.IsNullOrEmpty(p.UnsortedPath))
                {
                    p.UnsortedPath = Environment.ExpandEnvironmentVariables(p.UnsortedPath);
                    p.BasePath = Environment.ExpandEnvironmentVariables(p.BasePath);

                    var basePaths = Extensions.TryProcessMaskedPath(p.BasePath);

                    if (basePaths.Count==0)
                    {
                        s_logger.Error("Base folder is not available. Value provided: {0}", p.BasePath);
                        return;
                    }

                    if (!Directory.Exists(p.UnsortedPath))
                    {
                        s_logger.Error("Unsorted folder is not available. Value provided: {0}", p.UnsortedPath);
                        return;
                    }

                    var stat = ProcessPath(basePaths, p.UnsortedPath, p.ExtList);

                    if (stat.MissingFiles.Count == 0 && stat.DifferentFiles.Count == 0)
                    {
                        s_logger.Info("All files from {0} are present in {1}", p.UnsortedPath, p.BasePath);
                    }
                    else
                    {
                        if (stat.MissingFiles.Count > 0)
                        {
                            s_logger.Info("Some files from {0} are not present in {1}", p.UnsortedPath, p.BasePath);
                            foreach (var fileInfo in stat.MissingFiles)
                            {
                                s_logger.Info(fileInfo.FullName);
                            }

                            if (!string.IsNullOrEmpty(p.OutputMissingFileName))
                            {
                                File.WriteAllLines(Environment.ExpandEnvironmentVariables(p.OutputMissingFileName), stat.MissingFiles.Select(_ => _.FullName));
                            }
                        }

                        if (stat.DifferentFiles.Count > 0)
                        {
                            s_logger.Info("Some files differ between {0} and {1}", p.UnsortedPath, p.BasePath);
                            foreach (var fileInfo in stat.DifferentFiles)
                            {
                                s_logger.Info(fileInfo.FullName);
                            }
                            if (!string.IsNullOrEmpty(p.OutputDifferenceFileName))
                            {
                                File.WriteAllLines(Environment.ExpandEnvironmentVariables(p.OutputDifferenceFileName), stat.DifferentFiles.Select(_ => _.FullName));
                            }
                        }
                    }
                }
            }
        }

        private static Params ParseParams(string[] args)
        {
            var p = new Params();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case PARAM_BASE:
                        p.BasePath = GetParamValueOrThrow(args, ref i, $"Command-line parameter {PARAM_BASE} needs path");
                        break;
                    case PARAM_UNSORTED:
                        p.UnsortedPath = GetParamValueOrThrow(args, ref i, $"Command-line parameter {PARAM_UNSORTED} needs path");
                        break;
                    case PARAM_EXT:
                        p.ExtList = GetParamValueOrThrow(args, ref i, $"Command-line parameter {PARAM_EXT} needs comma-separated list of extensions");
                        break;
                    case PARAM_MISS:
                        p.OutputMissingFileName = GetParamValueOrThrow(args, ref i, $"Command-line parameter {PARAM_MISS} needs valid file path/name");
                        break;
                    case PARAM_DIFF:
                        p.OutputDifferenceFileName = GetParamValueOrThrow(args, ref i, $"Command-line parameter {PARAM_DIFF} needs valid file path/name");
                        break;
                    default:
                        throw new ApplicationException("Unknown command-line parameter");
                }
            }

            return p;
        }

        private static string GetParamValueOrThrow(string[] args, ref int i, string errorMessage)
        {
            string s;
            i++;
            if (args.Length > i)
            {
                s = args[i];
            }
            else
            {
                throw new ApplicationException(errorMessage);
            }

            return s;
        }

        private static void ShowHelp()
        {
            s_logger.Info($"wfam.exe {PARAM_BASE} <BASE_FOLDER> {PARAM_UNSORTED} <UNSORTED_FOLDER>");
            s_logger.Info($"wfam.exe {PARAM_BASE} <BASE_FOLDER> {PARAM_UNSORTED} <UNSORTED_FOLDER> {PARAM_EXT} <EXT_LIST>");
            s_logger.Info($"wfam.exe {PARAM_BASE} <BASE_FOLDER> {PARAM_UNSORTED} <UNSORTED_FOLDER> {PARAM_EXT} <EXT_LIST> {PARAM_MISS} <MISSING_FILE_LIST> {PARAM_DIFF} <DIFFERENCE_FILE_LIST>");
            s_logger.Info("Where");
            s_logger.Info("\t<BASE_FOLDER> is root of sorted file storage, i.e. photos by date");
            s_logger.Info("\t<UNSORTED_FOLDER> is a root of files to check for presense in <BASE_FOLDER>");
            s_logger.Info("\t<EXT_LIST> is an optional comma-separated list of extentions to process, i.e. .jpg,.jpeg");
            s_logger.Info("\t<MISSING_FILE_LIST> is an optional name of file to save list of missing files into");
            s_logger.Info("\t<DIFFERENCE_FILE_LIST> is an optional name of file to save list of files which are present but different into");
        }

        private static Stats ProcessPath(IEnumerable<string> basePaths, string unsortedPath, string extList)
        {
            HashSet<string> validExt;
            if (!string.IsNullOrEmpty(extList))
            {
                validExt = new HashSet<string>(extList.Split(",", StringSplitOptions.RemoveEmptyEntries).Where(_=>_.StartsWith('.')), StringComparer.OrdinalIgnoreCase);
                if (validExt.Count == 0)
                {
                    validExt = null;
                }
            }
            else
            {
                validExt = null;
            }
            var result = new Stats();

            var data = new Dictionary<string, List<FileInfo>>();

            foreach (var basePath in basePaths)
            {
                var d = ReadBaseFolderContent(basePath, validExt);
                data.Merge<string, List<FileInfo>, FileInfo>(d);
            }

            s_logger.Trace("Internal map built. {0} items in the map", data.Count);

            var unsortedEntries = Directory.GetFileSystemEntries(unsortedPath, "*", SearchOption.AllDirectories);

            foreach (var s in unsortedEntries)
            {
                var fullPath = new FileInfo(Path.Combine(unsortedPath, s));

                if ((fullPath.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    continue;
                }


                if (validExt != null && !validExt.Contains(Path.GetExtension(s)))
                {
                    s_logger.Trace("Unmatched unsorted file extenstion, skipping {0}", s);
                    continue;
                }

                var fn = Path.GetFileName(s);

                FileInfo found;
                bool nameExists;
                if (data.TryGetValue(fn, out var list))
                {
                    found = list.FirstOrDefault(_ => _.Length == fullPath.Length);
                    nameExists = true;
                }
                else
                {
                    found = null;
                    nameExists = false;
                }

                if (found == null)
                {
                    if (nameExists)
                    {
                        s_logger.Trace("{0} is found in base, but different", fullPath.FullName);
                        result.DifferentFiles.Add(fullPath);
                    }
                    else
                    {
                        s_logger.Trace("{0} is not found in base", fullPath.FullName);
                        result.MissingFiles.Add(fullPath);
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, List<FileInfo>> ReadBaseFolderContent(string basePath, ICollection<string> validExt)
        {
            var data = new Dictionary<string, List<FileInfo>>();

            s_logger.Trace("Reading base folder content for {0}", basePath);
            var sourceEntries = Directory.GetFileSystemEntries(basePath, "*", SearchOption.AllDirectories);

            s_logger.Trace("{0} file entries were read", sourceEntries.Length);

            s_logger.Trace("Building internal map - started for {0}", basePath);

            foreach (var s in sourceEntries)
            {
                var fullPath = new FileInfo(Path.Combine(basePath, s));

                if ((fullPath.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    continue;
                }

                if (validExt != null && !validExt.Contains(Path.GetExtension(s)))
                {
                    s_logger.Trace("Unmatched file extenstion, skipping {0}", s);
                    continue;
                }

                var fn = Path.GetFileName(s);
                if (!data.ContainsKey(fn))
                {
                    data.Add(fn, new List<FileInfo>());
                }

                var l = data[fn];
                l.Add(fullPath);
            }

            s_logger.Trace("Building internal map - completed for {0}", basePath);

            return data;
        }
    }

    class Stats
    {
        public readonly List<FileInfo> MissingFiles = new List<FileInfo>();
        public readonly List<FileInfo> DifferentFiles = new List<FileInfo>();
    }

    internal static class Extensions
    {
        public static void Merge<K, V, Z>(this IDictionary<K, V> target, IDictionary<K, V> source) where V:ICollection<Z>
        {
            foreach (var kvp in source)
            {
                if (target.TryGetValue(kvp.Key, out var val))
                {
                    foreach (var t in kvp.Value)
                    {
                        val.Add(t);
                    }
                }
                else
                {
                    target.Add(kvp);
                }
            }
        }

        public static List<string> TryProcessMaskedPath(string basePath)
        {
            basePath = Path.TrimEndingDirectorySeparator(basePath);

            List<string> result = new List<string>();
            var wildcardCharIndex = basePath.IndexOfAny(new[] {'*', '?'});
            if (wildcardCharIndex < 0)
            {
                result.Add(basePath);
            }
            else
            {
                var i = wildcardCharIndex - 1;
                while(i>=0 && !IsDirectorySeparator(basePath[i]))
                {
                    i--;
                }

                var levels = 0;
                for (var y = wildcardCharIndex; y < basePath.Length; y++)
                {
                    if (IsDirectorySeparator(basePath[y]))
                    {
                        levels++;
                    }
                }

                var root = i == 0 ? string.Empty : basePath.Substring(0, i);

                var entries = Directory.GetDirectories(root, "*");

                for(var level=0;level<levels;level++)
                {
                    var curr = entries.SelectMany(_ =>Directory.GetDirectories( _, "*")).ToArray();
                    entries = curr;
                }

                
                var pattern =
                    '^' +
                    Regex.Escape(basePath.Replace(".", "__DOT__")
                            .Replace("*", "__STAR__")
                            .Replace("?", "__QM__"))
                        .Replace("__DOT__", "[.]")
                        .Replace("__STAR__", ".*")
                        .Replace("__QM__", ".")
                    + '$';
                var re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

                foreach (var s in entries)
                {
                    var p = Path.Combine(root, s);
                    if(re.IsMatch(p))
                    {
                        result.Add(p);
                    }
                }
            }

            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsDirectorySeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
