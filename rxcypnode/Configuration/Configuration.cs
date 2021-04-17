using rxcypnode.UI;

namespace rxcypnode.Configuration
{
    public class Configuration
    {
        private IUserInterface _userInterface;

        public Configuration(IUserInterface userInterface)
        {
            _userInterface = userInterface;

            var networkConfiguration = new Network(userInterface);
            if (!networkConfiguration.Do())
            {
                Cancel();
                return;
            }
        }

        private void Cancel()
        {
            var section = new UserInterfaceSection(
                "Cancel configuration",
                "Configuration cancelled",
                null);

            _userInterface.Do(section);
        }
    }
}