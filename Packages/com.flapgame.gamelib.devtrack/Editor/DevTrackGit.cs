using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FlapGame.GameLib.DevTrack
{
    /// <summary>What the dev's checkout is: the <c>origin</c> url, the branch, the commit and the committer.</summary>
    public sealed class DevTrackGitInfo
    {
        public string Url { get; set; }
        public string Branch { get; set; }
        public string Commit { get; set; }
        public string UserName { get; set; }
        public string UserEmail { get; set; }

        public bool IsEmpty =>
            string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(Branch) && string.IsNullOrEmpty(Commit) &&
            string.IsNullOrEmpty(UserName) && string.IsNullOrEmpty(UserEmail);

        /// <summary>Takes every member <paramref name="other"/> has and this one does not.</summary>
        public void FillFrom(DevTrackGitInfo other)
        {
            if (other == null) return;
            if (string.IsNullOrEmpty(Url)) Url = other.Url;
            if (string.IsNullOrEmpty(Branch)) Branch = other.Branch;
            if (string.IsNullOrEmpty(Commit)) Commit = other.Commit;
            if (string.IsNullOrEmpty(UserName)) UserName = other.UserName;
            if (string.IsNullOrEmpty(UserEmail)) UserEmail = other.UserEmail;
        }
    }

    /// <summary>
    /// Reads the git checkout the Unity project sits in. <c>git</c> itself is asked first, because only it sees the
    /// global config and <c>include</c> directives (that is where <c>user.name</c> usually lives); when it is not on
    /// PATH the <c>.git</c> files are parsed instead. The repository root may be above the Unity project folder.
    /// Nothing here throws: a dev without git, or with a project outside a repository, simply reports empty fields.
    /// </summary>
    public static class DevTrackGit
    {
        /// <summary>Per <c>git</c> call. Four calls, so the whole probe stays under a second even on a cold cache.</summary>
        public const int DefaultTimeoutMilliseconds = 2000;

        /// <summary>How far up from the project folder a <c>.git</c> is looked for.</summary>
        public const int MaxParentDirectories = 12;

        public static DevTrackGitInfo Read(string startDirectory, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            var info = new DevTrackGitInfo();
            if (string.IsNullOrEmpty(startDirectory)) return info;

            try { info = ReadFromGit(startDirectory, timeoutMilliseconds); }
            catch (Exception) { info = new DevTrackGitInfo(); }

            try
            {
                string root = FindRepositoryRoot(startDirectory);
                if (root != null) info.FillFrom(ReadFromFiles(root));
            }
            catch (Exception) { }

            return info;
        }

        /// <summary>Asks <c>git</c>. Every member is empty when git is missing or the folder is not a repository.</summary>
        public static DevTrackGitInfo ReadFromGit(string workingDirectory, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            return new DevTrackGitInfo
            {
                Url = Run(workingDirectory, "remote get-url origin", timeoutMilliseconds),
                Branch = NullIfDetached(Run(workingDirectory, "rev-parse --abbrev-ref HEAD", timeoutMilliseconds)),
                Commit = Run(workingDirectory, "rev-parse HEAD", timeoutMilliseconds),
                UserName = Run(workingDirectory, "config --get user.name", timeoutMilliseconds),
                UserEmail = Run(workingDirectory, "config --get user.email", timeoutMilliseconds),
            };
        }

        /// <summary>
        /// The fallback: <c>.git/HEAD</c>, <c>.git/refs/heads/&lt;branch&gt;</c> or <c>.git/packed-refs</c>, and
        /// <c>.git/config</c>. Only the repository's own config is read, so <c>user.name</c> is usually empty here -
        /// it lives in the dev's global config, which only <c>git</c> itself resolves.
        /// </summary>
        public static DevTrackGitInfo ReadFromFiles(string repositoryRoot)
        {
            var info = new DevTrackGitInfo();
            string gitDirectory = ResolveGitDirectory(repositoryRoot);
            if (gitDirectory == null) return info;

            string head = ReadAllText(Path.Combine(gitDirectory, "HEAD"));
            if (!string.IsNullOrEmpty(head))
            {
                head = head.Trim();
                if (head.StartsWith("ref:", StringComparison.Ordinal))
                {
                    string reference = head.Substring(4).Trim();
                    info.Branch = ShortBranchName(reference);
                    info.Commit = ReadReference(gitDirectory, reference);
                }
                else if (IsCommitSha(head))
                {
                    // Detached HEAD: there is no branch name to report.
                    info.Commit = head;
                }
            }

            string config = ReadAllText(Path.Combine(gitDirectory, "config"));
            if (!string.IsNullOrEmpty(config))
            {
                info.Url = ReadConfigValue(config, "remote", "origin", "url");
                info.UserName = ReadConfigValue(config, "user", null, "name");
                info.UserEmail = ReadConfigValue(config, "user", null, "email");
            }

            return info;
        }

        /// <summary>
        /// The nearest folder at or above <paramref name="startDirectory"/> that holds a <c>.git</c>, or null. The Unity
        /// project is often a subfolder of the repository.
        /// </summary>
        public static string FindRepositoryRoot(string startDirectory)
        {
            if (string.IsNullOrEmpty(startDirectory)) return null;

            DirectoryInfo directory;
            try { directory = new DirectoryInfo(startDirectory); }
            catch (Exception) { return null; }

            for (int i = 0; i < MaxParentDirectories && directory != null; i++)
            {
                string candidate = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(candidate) || File.Exists(candidate)) return directory.FullName;
                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>
        /// The real git directory of <paramref name="repositoryRoot"/>: <c>.git</c> itself, or, when that is a file
        /// (a worktree or a submodule), the path its <c>gitdir:</c> line names. Null when there is none.
        /// </summary>
        public static string ResolveGitDirectory(string repositoryRoot)
        {
            if (string.IsNullOrEmpty(repositoryRoot)) return null;

            string candidate = Path.Combine(repositoryRoot, ".git");
            if (Directory.Exists(candidate)) return candidate;
            if (!File.Exists(candidate)) return null;

            string contents = ReadAllText(candidate);
            if (string.IsNullOrEmpty(contents)) return null;

            foreach (string line in contents.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase)) continue;

                string path = trimmed.Substring("gitdir:".Length).Trim();
                if (path.Length == 0) continue;
                try
                {
                    string full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(repositoryRoot, path));
                    return Directory.Exists(full) ? full : null;
                }
                catch (Exception) { return null; }
            }

            return null;
        }

        /// <summary>
        /// One value out of a git config file: <c>[user] name</c> as <c>("user", null, "name")</c>,
        /// <c>[remote "origin"] url</c> as <c>("remote", "origin", "url")</c>. Section and key names are
        /// case-insensitive, the subsection name is not - as git itself has it. The last value wins, as in git.
        /// </summary>
        public static string ReadConfigValue(string configContents, string section, string subsection, string key)
        {
            if (string.IsNullOrEmpty(configContents) || string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key)) return null;

            string result = null;
            bool inSection = false;

            foreach (string rawLine in configContents.Split('\n'))
            {
                string line = rawLine.Trim().TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    inSection = end > 0 && HeaderMatches(line.Substring(1, end - 1).Trim(), section, subsection);
                    continue;
                }

                if (!inSection) continue;

                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                if (!string.Equals(line.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;

                string value = line.Substring(equals + 1).Trim();
                if (value.Length > 0 && value[0] == '"')
                {
                    // A quoted value may hold a '#'; only what is inside the quotes counts.
                    int closing = value.IndexOf('"', 1);
                    value = closing > 0 ? value.Substring(1, closing - 1) : value.Substring(1);
                }
                else
                {
                    int comment = value.IndexOfAny(new[] { '#', ';' });
                    if (comment >= 0) value = value.Substring(0, comment).Trim();
                }

                if (value.Length > 0) result = value;
            }

            return result;
        }

        /// <summary><c>refs/heads/feat/devtrack</c> -&gt; <c>feat/devtrack</c>.</summary>
        public static string ShortBranchName(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            const string prefix = "refs/heads/";
            return reference.StartsWith(prefix, StringComparison.Ordinal) ? reference.Substring(prefix.Length) : reference;
        }

        /// <summary>The sha a ref points at: the loose ref file, else the <c>packed-refs</c> line.</summary>
        private static string ReadReference(string gitDirectory, string reference)
        {
            string loose = ReadAllText(Path.Combine(gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar)));
            if (!string.IsNullOrEmpty(loose))
            {
                string sha = loose.Trim();
                if (IsCommitSha(sha)) return sha;
            }

            string packed = ReadAllText(Path.Combine(gitDirectory, "packed-refs"));
            if (string.IsNullOrEmpty(packed)) return null;

            foreach (string rawLine in packed.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '^') continue;

                int space = line.IndexOf(' ');
                if (space <= 0) continue;
                if (!string.Equals(line.Substring(space + 1).Trim(), reference, StringComparison.Ordinal)) continue;

                string sha = line.Substring(0, space);
                if (IsCommitSha(sha)) return sha;
            }

            return null;
        }

        private static bool HeaderMatches(string header, string section, string subsection)
        {
            int quote = header.IndexOf('"');
            if (quote < 0)
            {
                // [user], or the legacy [remote.origin] form.
                if (subsection == null) return string.Equals(header, section, StringComparison.OrdinalIgnoreCase);
                return string.Equals(header, section + "." + subsection, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.Equals(header.Substring(0, quote).Trim(), section, StringComparison.OrdinalIgnoreCase)) return false;

            int closing = header.LastIndexOf('"');
            if (closing <= quote) return false;
            return string.Equals(header.Substring(quote + 1, closing - quote - 1), subsection, StringComparison.Ordinal);
        }

        private static bool IsCommitSha(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 7 || value.Length > 64) return false;
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static string NullIfDetached(string branch) =>
            string.Equals(branch, "HEAD", StringComparison.Ordinal) ? null : branch;

        private static string ReadAllText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// One <c>git</c> call, output trimmed to its first line, null on anything that is not a clean exit. Output is
        /// read through the asynchronous handlers so a hung git is killed by the timeout instead of blocking forever on
        /// a read.
        /// </summary>
        public static string Run(string workingDirectory, string arguments, int timeoutMilliseconds)
        {
            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory)) return null;

            var output = new StringBuilder(128);
            try
            {
                var startInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) lock (output) output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (_, e) => { };

                if (!process.Start()) return null;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try { process.Kill(); }
                    catch (Exception) { }
                    return null;
                }

                if (process.ExitCode != 0) return null;
            }
            catch (Exception)
            {
                // No git on PATH, or it cannot be started: the file fallback takes over.
                return null;
            }

            string text;
            lock (output) text = output.ToString();

            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }

            return null;
        }
    }
}
