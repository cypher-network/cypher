// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Models
{
    public class StakingConfigurationOptions
    {
        public double Distribution { get; set; }
        public bool OnOff { get; set; }
        public WalletSettings WalletSettings { get; set; }
    }
}
