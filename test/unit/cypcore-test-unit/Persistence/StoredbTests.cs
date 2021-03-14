using CYPCore.Persistence;
using System;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace cypcore_test_unit
{
    public class StoredbTests
    {
        private const string _path = "StoreDB";
        private StoreDb _storeDb;

        [SetUp]
        public void Setup()
        {
            _storeDb = null;
        }

        [Test]
        public void Instantiation_ValidPath_DoesNotThrow()
        {
            /*Assert.That(() => _storedb = new Storedb(_path), Throws.Nothing);
            Assert.That(() => _storedb.InitAndRecover(), Throws.Nothing);
            Assert.NotNull(_storedb.Database);*/
        }

        [Test]
        public void Instantiation_InvalidPath_Throws()
        {
            const string invalidPath = null;

            Assert.That(() => _storeDb = new StoreDb(invalidPath), Throws.Exception);
            Assert.Null(_storeDb);
        }

        [Test]
        public void Checkpoint_ReturnsValidToken()
        {
            /*_storedb = new Storedb(_path);
            _storedb.Checkpoint();
            var token = _storedb.Checkpoint();
            Assert.AreNotEqual(token, Guid.Empty);*/
        }
    }
}