using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Octodiff.Tests.Util;

namespace Octodiff.Tests
{
    [TestFixture]
    public class SignatureFixture : CommandLineFixture
    {
        [Test]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage100mb.zip", 1000, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Async)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Async)]
        [TestCase("SmallPackage100mb.zip", 1000, OctodiffAppVariant.Async)]
        public void ShouldCreateSignature(string name, int numberOfFiles, OctodiffAppVariant octodiff)
        {
            PackageGenerator.GeneratePackage(name, numberOfFiles);

            Run("signature " + name + " " + name + ".sig", octodiff);
            Assert.That(ExitCode, Is.EqualTo(0));

            var basisSize = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, name)).Length;
            var signatureSize = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig").Length;
            var signatureSizePercentageOfBasis = signatureSize/(double) basisSize;

            Trace.WriteLine(string.Format("Basis size: {0:n0}", basisSize));
            Trace.WriteLine(string.Format("Signature size: {0:n0}", signatureSize));
            Trace.WriteLine(string.Format("Signature ratio: {0:n3}", signatureSizePercentageOfBasis));
            Assert.That(0.006 <= signatureSizePercentageOfBasis && signatureSizePercentageOfBasis <= 0.014, Is.True);
        }

        [Test]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage100mb.zip", 1000, OctodiffAppVariant.Sync)]
        [TestCase("SmallPackage1mb.zip", 10, OctodiffAppVariant.Async)]
        [TestCase("SmallPackage10mb.zip", 100, OctodiffAppVariant.Async)]
        [TestCase("SmallPackage100mb.zip", 1000, OctodiffAppVariant.Async)]
        public void ShouldCreateDifferentSignaturesBasedOnChunkSize(string name, int numberOfFiles, OctodiffAppVariant octodiff)
        {
            PackageGenerator.GeneratePackage(name, numberOfFiles);

            Run("signature " + name + " " + name + ".sig.1 --chunk-size=128", octodiff);
            Run("signature " + name + " " + name + ".sig.2 --chunk-size=256", octodiff);
            Run("signature " + name + " " + name + ".sig.3 --chunk-size=1024", octodiff);
            Run("signature " + name + " " + name + ".sig.4 --chunk-size=2048", octodiff);
            Run("signature " + name + " " + name + ".sig.5 --chunk-size=31744", octodiff);

            Assert.That(Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.1") 
                > Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.2"));
            Assert.That(Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.2") 
                > Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.3"));
            Assert.That(Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.3") 
                > Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.4"));
            Assert.That(Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.4") 
                > Length(Path.Combine(TestContext.CurrentContext.TestDirectory, name) + ".sig.5"));
        }

        static long Length(string fileName)
        {
            return new FileInfo(fileName).Length;
        }
    }
}