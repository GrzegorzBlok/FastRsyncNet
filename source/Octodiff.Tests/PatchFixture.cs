using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using Octodiff.Tests.Util;

namespace Octodiff.Tests
{
    [TestFixture]
    public class PatchFixture : CommandLineFixture
    {
        [Test]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage100mb.zip", 1000, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Async)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Async)]
        [TestCase("SmallPackage100mb.zip", 1000, OctodiffAppVariant.Async)]
        public void PatchingShouldResultInPerfectCopy(string name, int numberOfFiles, OctodiffAppVariant octodiff)
        {
            var newName = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "2.zip");
            var copyName = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "2_out.zip");
            PackageGenerator.GeneratePackage(name, numberOfFiles);
            PackageGenerator.ModifyPackage(name, newName, (int)(0.33 * numberOfFiles), (int)(0.10 * numberOfFiles));

            Run("signature " + name + " " + name + ".sig", octodiff);
            Run("delta " + name + ".sig " + newName + " " + name + ".delta", octodiff);
            Run("patch " + name + " " + name + ".delta" + " " + copyName, octodiff);
            Assert.That(ExitCode, Is.EqualTo(0));

            Assert.That(Sha1(newName), Is.EqualTo(Sha1(copyName)));
        }

        [Test]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Async)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Async)]
        public void PatchVerificationShouldFailWhenFilesModified(string name, int numberOfFiles, OctodiffAppVariant octodiff)
        {
            var newBasis = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "1.zip");
            var newName = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "2.zip");
            var copyName = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "2_out.zip");
            PackageGenerator.GeneratePackage(name, numberOfFiles);
            PackageGenerator.ModifyPackage(name, newBasis, numberOfFiles, (int)(0.5 * numberOfFiles));
            PackageGenerator.ModifyPackage(name, newName, (int)(0.33 * numberOfFiles), (int)(0.10 * numberOfFiles));

            Run("signature " + name + " " + name + ".sig", octodiff);
            Run("delta " + name + ".sig " + newName + " " + name + ".delta", octodiff);
            Run("patch " + newBasis + " " + name + ".delta" + " " + copyName, octodiff);
            Assert.That(ExitCode, Is.EqualTo(2));
            Assert.That(Output, Does.Contain("Error: Verification of the patched file failed")
                .Or.Contain("does not match the one the delta was built against"));
        }

        [Test]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Async)]
        public void PatchVerificationCanBeSkipped(string name, int numberOfFiles, OctodiffAppVariant octodiff)
        {
            var newBasis = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "1.zip");
            var newName = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "2.zip");
            var copyName = Path.ChangeExtension(Path.Combine(TestContext.CurrentContext.TestDirectory, name), "2_out.zip");
            PackageGenerator.GeneratePackage(name, numberOfFiles);
            PackageGenerator.ModifyPackage(name, newBasis, (int)(0.33 * numberOfFiles), (int)(0.10 * numberOfFiles));
            PackageGenerator.ModifyPackage(name, newName, (int)(0.33 * numberOfFiles), (int)(0.10 * numberOfFiles));

            Run("signature " + name + " " + name + ".sig", octodiff);
            Run("delta " + name + ".sig " + newName + " " + name + ".delta", octodiff);
            Run("patch " + newBasis + " " + name + ".delta" + " " + copyName + " --skip-verification", octodiff);
            Assert.That(ExitCode, Is.EqualTo(0));
            Assert.That(Sha1(newName), Is.Not.EqualTo(Sha1(copyName)));
        }

        static string Sha1(string fileName)
        {
            using (var s = new FileStream(fileName, FileMode.Open))
            {
                return BitConverter.ToString(SHA1.Create().ComputeHash(s)).Replace("-", "");
            }
        }
    }
}