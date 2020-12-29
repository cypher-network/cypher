// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

namespace CYPCore.Models
{
    public class WalletSettings
    {
        public string Address { get; set; }
        public string Identifier { get; set; }
        public string Passphrase { get; set; }
        public string SendPaymentEndpoint { get; set; }
        public string Url { get; set; }
    }
}
