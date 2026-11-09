using System.IO;
using FlapGame.GameLib.DevTrack;
using NUnit.Framework;

namespace FlapGame.GameLib.Tests
{
    /// <summary>
    /// The fallback that runs when <c>git</c> is not on PATH: a fake <c>.git</c> on disk is parsed. Asking <c>git</c>
    /// itself is not tested - it needs the dev's own installation and checkout.
    /// </summary>
    public class DevTrackGitTests
    {
        private string _root;

        private const string Config =
            "[core]\n" +
            "\trepositoryformatversion = 0\n" +
            "\tbare = false\n" +
            "[remote \"upstream\"]\n" +
            "\turl = https://example.com/not-origin.git\n" +
            "[remote \"origin\"]\n" +
            "\turl = https://github.com/flap-game/GameLib.git\n" +
            "\tfetch = +refs/heads/*:refs/remotes/origin/*\n" +
            "[user]\n" +
            "\tname = Tuan Anh Tran\n" +
            "\temail = tuananh@flapgame.com\n";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gamelib-devtrack-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, ".git"));
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (IOException) { }
        }

        private void Write(string relativePath, string contents)
        {
            string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
        }

        [Test]
        public void ReadFromFiles_TakesTheBranch_TheLooseRef_OriginAndTheUser()
        {
            Write(".git/HEAD", "ref: refs/heads/feat/devtrack\n");
            Write(".git/refs/heads/feat/devtrack", "5f64b61a0c9e4d2b8a71f0c3d4e5f6a7b8c9d0e1\n");
            Write(".git/config", Config);

            DevTrackGitInfo info = DevTrackGit.ReadFromFiles(_root);

            Assert.That(info.Branch, Is.EqualTo("feat/devtrack"));
            Assert.That(info.Commit, Is.EqualTo("5f64b61a0c9e4d2b8a71f0c3d4e5f6a7b8c9d0e1"));
            Assert.That(info.Url, Is.EqualTo("https://github.com/flap-game/GameLib.git"), "origin, not the other remote");
            Assert.That(info.UserName, Is.EqualTo("Tuan Anh Tran"));
            Assert.That(info.UserEmail, Is.EqualTo("tuananh@flapgame.com"));
        }

        [Test]
        public void ReadFromFiles_FallsBackToPackedRefs()
        {
            Write(".git/HEAD", "ref: refs/heads/main\n");
            Write(".git/packed-refs",
                  "# pack-refs with: peeled fully-peeled sorted \n" +
                  "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa refs/heads/other\n" +
                  "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb refs/heads/main\n" +
                  "cccccccccccccccccccccccccccccccccccccccc refs/tags/v0.9.1\n" +
                  "^dddddddddddddddddddddddddddddddddddddddd\n");

            DevTrackGitInfo info = DevTrackGit.ReadFromFiles(_root);

            Assert.That(info.Branch, Is.EqualTo("main"));
            Assert.That(info.Commit, Is.EqualTo("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
        }

        [Test]
        public void ReadFromFiles_OnADetachedHead_HasTheCommitAndNoBranch()
        {
            Write(".git/HEAD", "5f64b61a0c9e4d2b8a71f0c3d4e5f6a7b8c9d0e1\n");

            DevTrackGitInfo info = DevTrackGit.ReadFromFiles(_root);

            Assert.That(info.Commit, Is.EqualTo("5f64b61a0c9e4d2b8a71f0c3d4e5f6a7b8c9d0e1"));
            Assert.That(info.Branch, Is.Null);
        }

        [Test]
        public void ReadFromFiles_WithoutAGitFolder_IsEmptyAndDoesNotThrow()
        {
            Directory.Delete(Path.Combine(_root, ".git"), true);

            Assert.That(DevTrackGit.ReadFromFiles(_root).IsEmpty, Is.True);
            Assert.That(DevTrackGit.ReadFromFiles(null).IsEmpty, Is.True);
            Assert.That(DevTrackGit.ReadFromFiles(Path.Combine(_root, "nope")).IsEmpty, Is.True);
        }

        [Test]
        public void FindRepositoryRoot_LooksUpward_BecauseTheUnityProjectMayBeASubfolder()
        {
            Write(".git/HEAD", "ref: refs/heads/main\n");
            string project = Path.Combine(_root, "UnityProjects", "word-jigsort");
            Directory.CreateDirectory(project);

            Assert.That(DevTrackGit.FindRepositoryRoot(project), Is.EqualTo(_root));
        }

        [Test]
        public void FindRepositoryRoot_IsNullOutsideARepository()
        {
            Directory.Delete(Path.Combine(_root, ".git"), true);

            Assert.That(DevTrackGit.FindRepositoryRoot(_root), Is.Null);
        }

        [Test]
        public void AGitFileInsteadOfAFolder_IsFollowed()
        {
            // A worktree or a submodule has ".git" as a file pointing at the real git directory.
            string real = Path.Combine(_root, "real-git-dir");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "HEAD"), "ref: refs/heads/main\n");
            Directory.CreateDirectory(Path.Combine(real, "refs", "heads"));
            File.WriteAllText(Path.Combine(real, "refs", "heads", "main"), "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n");

            Directory.Delete(Path.Combine(_root, ".git"), true);
            File.WriteAllText(Path.Combine(_root, ".git"), "gitdir: real-git-dir\n");

            DevTrackGitInfo info = DevTrackGit.ReadFromFiles(_root);

            Assert.That(info.Branch, Is.EqualTo("main"));
            Assert.That(info.Commit, Is.EqualTo("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"));
        }

        [Test]
        public void ReadConfigValue_TakesTheRightSection()
        {
            Assert.That(DevTrackGit.ReadConfigValue(Config, "remote", "origin", "url"),
                        Is.EqualTo("https://github.com/flap-game/GameLib.git"));
            Assert.That(DevTrackGit.ReadConfigValue(Config, "remote", "upstream", "url"),
                        Is.EqualTo("https://example.com/not-origin.git"));
            Assert.That(DevTrackGit.ReadConfigValue(Config, "USER", null, "NAME"), Is.EqualTo("Tuan Anh Tran"),
                        "section and key names are case-insensitive");
            Assert.That(DevTrackGit.ReadConfigValue(Config, "user", null, "signingkey"), Is.Null);
            Assert.That(DevTrackGit.ReadConfigValue(Config, "remote", "ORIGIN", "url"), Is.Null,
                        "the subsection name is case-sensitive, as in git");
        }

        [Test]
        public void ReadConfigValue_IgnoresCommentsAndQuotes()
        {
            const string config = "; a comment\n" +
                                  "[user]\n" +
                                  "# name = wrong\n" +
                                  "\tname = \"Quoted Name\" ; trailing comment\n";

            Assert.That(DevTrackGit.ReadConfigValue(config, "user", null, "name"), Is.EqualTo("Quoted Name"));
        }

        [Test]
        public void ShortBranchName_DropsRefsHeads()
        {
            Assert.That(DevTrackGit.ShortBranchName("refs/heads/feat/a"), Is.EqualTo("feat/a"));
            Assert.That(DevTrackGit.ShortBranchName("main"), Is.EqualTo("main"));
            Assert.That(DevTrackGit.ShortBranchName(null), Is.Null);
        }
    }
}
