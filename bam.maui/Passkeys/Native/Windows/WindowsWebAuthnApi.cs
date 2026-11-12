using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Bam.Maui.Passkeys;

/// <summary>
/// The real <c>webauthn.dll</c>.  Structure layouts and versions follow Microsoft's published
/// <c>webauthn.h</c> (github.com/microsoft/webauthn).  Only the members of the versions named
/// below are declared; the library reads no further than the <c>dwVersion</c> it is given.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsWebAuthnApi : IWindowsWebAuthnApi
{
    private const string Library = "webauthn.dll";
    private const string PublicKey = "public-key";
    private const string Sha256 = "SHA-256";

    // WEBAUTHN_AUTHENTICATOR_MAKE_CREDENTIAL_OPTIONS version 4 (API version 3) adds bPreferResidentKey; version 3 is the API version 1 baseline.
    private const int MakeCredentialOptionsBaselineVersion = 3;
    private const int MakeCredentialOptionsPreferResidentKeyVersion = 4;
    private const int PreferResidentKeyApiVersion = 3;

    // WEBAUTHN_AUTHENTICATOR_GET_ASSERTION_OPTIONS version 4 is the API version 1 baseline.
    private const int GetAssertionOptionsVersion = 4;

    // WEBAUTHN_CREDENTIAL_ATTESTATION version 3 adds dwUsedTransport.
    private const int AttestationUsedTransportVersion = 3;

    public int GetApiVersionNumber()
    {
        return WebAuthNGetApiVersionNumber();
    }

    public bool IsUserVerifyingPlatformAuthenticatorAvailable()
    {
        int result = WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable(out int available);
        return result == WindowsPasskeyErrors.Success && available != 0;
    }

    public WindowsAttestation MakeCredential(IntPtr window, WindowsMakeCredentialCall call, CancellationToken cancellationToken)
    {
        using NativeBlocks blocks = new NativeBlocks();
        RpEntityInformation rp = new RpEntityInformation
        {
            dwVersion = 1,
            pwszId = blocks.Text(call.RelyingPartyId),
            pwszName = blocks.Text(call.RelyingPartyName)
        };
        UserEntityInformation user = new UserEntityInformation
        {
            dwVersion = 1,
            cbId = call.UserId.Length,
            pbId = blocks.Bytes(call.UserId),
            pwszName = blocks.Text(call.UserName),
            pwszDisplayName = blocks.Text(call.UserDisplayName)
        };
        IntPtr publicKey = blocks.Text(PublicKey);
        CoseCredentialParameter[] parameters = new CoseCredentialParameter[call.Algorithms.Count];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameters[i] = new CoseCredentialParameter { dwVersion = 1, pwszCredentialType = publicKey, lAlg = call.Algorithms[i] };
        }
        CoseCredentialParameters coseParameters = new CoseCredentialParameters
        {
            cCredentialParameters = parameters.Length,
            pCredentialParameters = blocks.Array(parameters)
        };
        ClientData clientData = NewClientData(blocks, call.ClientDataJson);

        bool canPrefer = GetApiVersionNumber() >= PreferResidentKeyApiVersion;
        MakeCredentialOptions options = new MakeCredentialOptions
        {
            dwVersion = canPrefer ? MakeCredentialOptionsPreferResidentKeyVersion : MakeCredentialOptionsBaselineVersion,
            dwTimeoutMilliseconds = call.TimeoutMilliseconds,
            CredentialList = NewCredentials(blocks, call.ExcludeCredentialIds, publicKey),
            dwAuthenticatorAttachment = call.AuthenticatorAttachment,
            bRequireResidentKey = call.RequireResidentKey ? 1 : 0,
            dwUserVerificationRequirement = call.UserVerificationRequirement,
            dwAttestationConveyancePreference = call.AttestationConveyancePreference,
            bPreferResidentKey = canPrefer && call.PreferResidentKey ? 1 : 0
        };

        using CancellationTokenRegistration cancellation = RegisterCancellation(blocks, cancellationToken, out IntPtr cancellationId);
        options.pCancellationId = cancellationId;
        if (cancellationToken.IsCancellationRequested)
        {
            // Cancelled before the prompt opened: there is no operation for the callback to have cancelled.
            return new WindowsAttestation { Result = WindowsPasskeyErrors.Win32Cancelled };
        }

        int result = WebAuthNAuthenticatorMakeCredential(window, ref rp, ref user, ref coseParameters, ref clientData, ref options, out IntPtr attestation);
        if (result != WindowsPasskeyErrors.Success || attestation == IntPtr.Zero)
        {
            return new WindowsAttestation { Result = result };
        }
        try
        {
            CredentialAttestation returned = Marshal.PtrToStructure<CredentialAttestation>(attestation);
            int usedTransport = 0;
            if (returned.dwVersion >= AttestationUsedTransportVersion)
            {
                usedTransport = Marshal.PtrToStructure<CredentialAttestationWithTransport>(attestation).dwUsedTransport;
            }
            return new WindowsAttestation
            {
                Result = result,
                AttestationObject = Copy(returned.pbAttestationObject, returned.cbAttestationObject),
                CredentialId = Copy(returned.pbCredentialId, returned.cbCredentialId),
                UsedTransport = usedTransport
            };
        }
        finally
        {
            WebAuthNFreeCredentialAttestation(attestation);
        }
    }

    public WindowsAssertion GetAssertion(IntPtr window, WindowsGetAssertionCall call, CancellationToken cancellationToken)
    {
        using NativeBlocks blocks = new NativeBlocks();
        IntPtr publicKey = blocks.Text(PublicKey);
        ClientData clientData = NewClientData(blocks, call.ClientDataJson);
        GetAssertionOptions options = new GetAssertionOptions
        {
            dwVersion = GetAssertionOptionsVersion,
            dwTimeoutMilliseconds = call.TimeoutMilliseconds,
            CredentialList = NewCredentials(blocks, call.AllowCredentialIds, publicKey),
            dwUserVerificationRequirement = call.UserVerificationRequirement
        };

        using CancellationTokenRegistration cancellation = RegisterCancellation(blocks, cancellationToken, out IntPtr cancellationId);
        options.pCancellationId = cancellationId;
        if (cancellationToken.IsCancellationRequested)
        {
            return new WindowsAssertion { Result = WindowsPasskeyErrors.Win32Cancelled };
        }

        int result = WebAuthNAuthenticatorGetAssertion(window, call.RelyingPartyId, ref clientData, ref options, out IntPtr assertion);
        if (result != WindowsPasskeyErrors.Success || assertion == IntPtr.Zero)
        {
            return new WindowsAssertion { Result = result };
        }
        try
        {
            Assertion returned = Marshal.PtrToStructure<Assertion>(assertion);
            return new WindowsAssertion
            {
                Result = result,
                AuthenticatorData = Copy(returned.pbAuthenticatorData, returned.cbAuthenticatorData),
                Signature = Copy(returned.pbSignature, returned.cbSignature),
                CredentialId = Copy(returned.Credential.pbId, returned.Credential.cbId),
                UserId = Copy(returned.pbUserId, returned.cbUserId)
            };
        }
        finally
        {
            WebAuthNFreeAssertion(assertion);
        }
    }

    private static ClientData NewClientData(NativeBlocks blocks, byte[] clientDataJson)
    {
        return new ClientData
        {
            dwVersion = 1,
            cbClientDataJSON = clientDataJson.Length,
            pbClientDataJSON = blocks.Bytes(clientDataJson),
            pwszHashAlgId = blocks.Text(Sha256)
        };
    }

    private static Credentials NewCredentials(NativeBlocks blocks, IReadOnlyList<byte[]> ids, IntPtr publicKey)
    {
        if (ids.Count == 0)
        {
            return default;
        }
        Credential[] credentials = new Credential[ids.Count];
        for (int i = 0; i < credentials.Length; i++)
        {
            credentials[i] = new Credential { dwVersion = 1, cbId = ids[i].Length, pbId = blocks.Bytes(ids[i]), pwszCredentialType = publicKey };
        }
        return new Credentials { cCredentials = credentials.Length, pCredentials = blocks.Array(credentials) };
    }

    // Gives the call a cancellation id and cancels it when the token fires.  Without an id the call still runs; it just cannot be cancelled.
    private static CancellationTokenRegistration RegisterCancellation(NativeBlocks blocks, CancellationToken cancellationToken, out IntPtr cancellationId)
    {
        cancellationId = IntPtr.Zero;
        if (!cancellationToken.CanBeCanceled || WebAuthNGetCancellationId(out Guid id) != WindowsPasskeyErrors.Success)
        {
            return default;
        }
        cancellationId = blocks.Value(id);
        return cancellationToken.Register(() =>
        {
            Guid copy = id;
            WebAuthNCancelCurrentOperation(ref copy);
        });
    }

    private static byte[]? Copy(IntPtr source, int length)
    {
        if (source == IntPtr.Zero || length <= 0)
        {
            return null;
        }
        byte[] bytes = new byte[length];
        Marshal.Copy(source, bytes, 0, length);
        return bytes;
    }

    [DllImport(Library, ExactSpelling = true)]
    private static extern int WebAuthNGetApiVersionNumber();

    [DllImport(Library, ExactSpelling = true)]
    private static extern int WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable(out int available);

    [DllImport(Library, ExactSpelling = true)]
    private static extern int WebAuthNAuthenticatorMakeCredential(IntPtr window, ref RpEntityInformation rp, ref UserEntityInformation user, ref CoseCredentialParameters parameters, ref ClientData clientData, ref MakeCredentialOptions options, out IntPtr attestation);

    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WebAuthNAuthenticatorGetAssertion(IntPtr window, string relyingPartyId, ref ClientData clientData, ref GetAssertionOptions options, out IntPtr assertion);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void WebAuthNFreeCredentialAttestation(IntPtr attestation);

    [DllImport(Library, ExactSpelling = true)]
    private static extern void WebAuthNFreeAssertion(IntPtr assertion);

    [DllImport(Library, ExactSpelling = true)]
    private static extern int WebAuthNGetCancellationId(out Guid cancellationId);

    [DllImport(Library, ExactSpelling = true)]
    private static extern int WebAuthNCancelCurrentOperation(ref Guid cancellationId);

    // WEBAUTHN_RP_ENTITY_INFORMATION
    [StructLayout(LayoutKind.Sequential)]
    internal struct RpEntityInformation
    {
        public int dwVersion;
        public IntPtr pwszId;
        public IntPtr pwszName;
        public IntPtr pwszIcon;
    }

    // WEBAUTHN_USER_ENTITY_INFORMATION
    [StructLayout(LayoutKind.Sequential)]
    internal struct UserEntityInformation
    {
        public int dwVersion;
        public int cbId;
        public IntPtr pbId;
        public IntPtr pwszName;
        public IntPtr pwszIcon;
        public IntPtr pwszDisplayName;
    }

    // WEBAUTHN_CLIENT_DATA
    [StructLayout(LayoutKind.Sequential)]
    internal struct ClientData
    {
        public int dwVersion;
        public int cbClientDataJSON;
        public IntPtr pbClientDataJSON;
        public IntPtr pwszHashAlgId;
    }

    // WEBAUTHN_COSE_CREDENTIAL_PARAMETER
    [StructLayout(LayoutKind.Sequential)]
    internal struct CoseCredentialParameter
    {
        public int dwVersion;
        public IntPtr pwszCredentialType;
        public int lAlg;
    }

    // WEBAUTHN_COSE_CREDENTIAL_PARAMETERS
    [StructLayout(LayoutKind.Sequential)]
    internal struct CoseCredentialParameters
    {
        public int cCredentialParameters;
        public IntPtr pCredentialParameters;
    }

    // WEBAUTHN_CREDENTIAL
    [StructLayout(LayoutKind.Sequential)]
    internal struct Credential
    {
        public int dwVersion;
        public int cbId;
        public IntPtr pbId;
        public IntPtr pwszCredentialType;
    }

    // WEBAUTHN_CREDENTIALS
    [StructLayout(LayoutKind.Sequential)]
    internal struct Credentials
    {
        public int cCredentials;
        public IntPtr pCredentials;
    }

    // WEBAUTHN_EXTENSIONS
    [StructLayout(LayoutKind.Sequential)]
    internal struct Extensions
    {
        public int cExtensions;
        public IntPtr pExtensions;
    }

    // WEBAUTHN_AUTHENTICATOR_MAKE_CREDENTIAL_OPTIONS, through version 4
    [StructLayout(LayoutKind.Sequential)]
    internal struct MakeCredentialOptions
    {
        public int dwVersion;
        public int dwTimeoutMilliseconds;
        public Credentials CredentialList;
        public Extensions Extensions;
        public int dwAuthenticatorAttachment;
        public int bRequireResidentKey;
        public int dwUserVerificationRequirement;
        public int dwAttestationConveyancePreference;
        public int dwFlags;
        public IntPtr pCancellationId;
        public IntPtr pExcludeCredentialList;
        public int dwEnterpriseAttestation;
        public int dwLargeBlobSupport;
        public int bPreferResidentKey;
    }

    // WEBAUTHN_AUTHENTICATOR_GET_ASSERTION_OPTIONS, through version 4
    [StructLayout(LayoutKind.Sequential)]
    internal struct GetAssertionOptions
    {
        public int dwVersion;
        public int dwTimeoutMilliseconds;
        public Credentials CredentialList;
        public Extensions Extensions;
        public int dwAuthenticatorAttachment;
        public int dwUserVerificationRequirement;
        public int dwFlags;
        public IntPtr pwszU2fAppId;
        public IntPtr pbU2fAppId;
        public IntPtr pCancellationId;
        public IntPtr pAllowCredentialList;
    }

    // WEBAUTHN_CREDENTIAL_ATTESTATION, version 1
    [StructLayout(LayoutKind.Sequential)]
    internal struct CredentialAttestation
    {
        public int dwVersion;
        public IntPtr pwszFormatType;
        public int cbAuthenticatorData;
        public IntPtr pbAuthenticatorData;
        public int cbAttestation;
        public IntPtr pbAttestation;
        public int dwAttestationDecodeType;
        public IntPtr pvAttestationDecode;
        public int cbAttestationObject;
        public IntPtr pbAttestationObject;
        public int cbCredentialId;
        public IntPtr pbCredentialId;
    }

    // WEBAUTHN_CREDENTIAL_ATTESTATION, through version 3
    [StructLayout(LayoutKind.Sequential)]
    internal struct CredentialAttestationWithTransport
    {
        public CredentialAttestation Baseline;
        public Extensions Extensions;
        public int dwUsedTransport;
    }

    // WEBAUTHN_ASSERTION, version 1
    [StructLayout(LayoutKind.Sequential)]
    internal struct Assertion
    {
        public int dwVersion;
        public int cbAuthenticatorData;
        public IntPtr pbAuthenticatorData;
        public int cbSignature;
        public IntPtr pbSignature;
        public Credential Credential;
        public int cbUserId;
        public IntPtr pbUserId;
    }

    /// <summary>Unmanaged memory that lives for one call and is freed together.</summary>
    private sealed class NativeBlocks : IDisposable
    {
        private readonly List<IntPtr> _blocks = new List<IntPtr>();

        public IntPtr Text(string? text)
        {
            if (text is null)
            {
                return IntPtr.Zero;
            }
            IntPtr block = Marshal.StringToHGlobalUni(text);
            _blocks.Add(block);
            return block;
        }

        public IntPtr Bytes(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return IntPtr.Zero;
            }
            IntPtr block = Allocate(bytes.Length);
            Marshal.Copy(bytes, 0, block, bytes.Length);
            return block;
        }

        public IntPtr Value<TValue>(TValue value) where TValue : struct
        {
            IntPtr block = Allocate(Marshal.SizeOf<TValue>());
            Marshal.StructureToPtr(value, block, false);
            return block;
        }

        public IntPtr Array<TValue>(TValue[] values) where TValue : struct
        {
            if (values.Length == 0)
            {
                return IntPtr.Zero;
            }
            int size = Marshal.SizeOf<TValue>();
            IntPtr block = Allocate(size * values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                Marshal.StructureToPtr(values[i], block + (i * size), false);
            }
            return block;
        }

        public void Dispose()
        {
            foreach (IntPtr block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }
            _blocks.Clear();
        }

        private IntPtr Allocate(int size)
        {
            IntPtr block = Marshal.AllocHGlobal(size);
            _blocks.Add(block);
            return block;
        }
    }
}
