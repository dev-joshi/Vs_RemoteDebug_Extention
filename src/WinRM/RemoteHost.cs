namespace RemoteDebug.WinRM
{
    using System;
    using System.Security;
    using Microsoft.Management.Infrastructure;
    using Microsoft.Management.Infrastructure.Options;

    internal class RemoteHost : IDisposable
    {
        public string Hostname { get; }

        public string Domain { get; }

        public string Username { get; }

        public string Password { get; }

        public CimSession CimSession
        {
            get
            {
                if (this.cimsession == null)
                {
                    var securePass = new SecureString();
                    foreach (char p in this.Password)
                        securePass.AppendChar(p);

                    var sessionOptions = new CimSessionOptions();
                    sessionOptions.AddDestinationCredentials(
                        new CimCredential(
                            PasswordAuthenticationMechanism.Negotiate,
                            this.Domain,
                            this.Username,
                            securePass));

                    this.cimsession = CimSession.Create(this.Hostname, sessionOptions);
                }

                return this.cimsession;
            }
        }

        public string ComputerName { get; set; }

        public RemoteHost(
            string hostname,
            string domain,
            string username,
            string password)
        {
            this.Hostname = hostname;
            this.Domain = domain;
            this.Username = username;
            this.Password = password;
        }

        public void Dispose()
        {
            this.CimSession?.Dispose();
        }

        private CimSession cimsession;
    }
}