﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Diagnostics;
using System.Text;

namespace System.Net.FtpClient {
	public delegate void FtpChannelConnected();
	public delegate void FtpChannelDisconnected();
	public delegate void FtpChannelDisposed();
	public delegate void FtpInvalidCertificate(FtpChannel c, InvalidCertificateInfo e);
	
	public abstract class FtpChannel : IDisposable {
		event FtpChannelConnected _onConnected = null;
		/// <summary>
		/// Event is fired after a connection has been made
		/// </summary>
		public event FtpChannelConnected ConnectionReady {
			add { _onConnected += value; }
			remove { _onConnected -= value; }
		}

		event FtpChannelDisconnected _onDisconnected = null;
		/// <summary>
		/// Event is fired when Disconnect is called
		/// </summary>
		public event FtpChannelDisconnected ConnectionClosed {
			add { _onDisconnected += value; }
			remove { _onDisconnected -= value; }
		}

		event FtpChannelDisposed _onDisposed = null;
		/// <summary>
		/// Event is fired when this object is disposed.
		/// </summary>
		public event FtpChannelDisposed Diposed {
			add { _onDisposed += value; }
			remove { _onDisposed -= value; }
		}

		event FtpInvalidCertificate _onBadCert = null;
		/// <summary>
		/// Event is fired when an invalid SSL certificate is encountered.
		/// </summary>
		public event FtpInvalidCertificate InvalidCertificate {
			add { _onBadCert += value; }
			remove { _onBadCert -= value; }
		}

		/// <summary>
		/// Fire ConnectionReady event
		/// </summary>
		protected void OnConnectionReady() {
			if (_onConnected != null) {
				this._onConnected();
			}
		}

		/// <summary>
		/// Fire ConnectionClosed event
		/// </summary>
		protected void OnConnectionClosed() {
			if (_onDisconnected != null) {
				this._onDisconnected();
			}
		}

		/// <summary>
		/// Fire Disposed event
		/// </summary>
		protected void OnDisposed() {
			if (_onDisposed != null) {
				this._onDisposed();
			}
		}

		/// <summary>
		/// Fire the invalid ssl certificate event
		/// </summary>
		protected void OnInvalidSslCerticate(FtpChannel c, InvalidCertificateInfo e) {
			if (this._onBadCert != null) {
				this._onBadCert(c, e);
			}
		}

		private bool _isServerSocket = false;
		/// <summary>
		/// Indicates if this is an incomming (active) or outgoing channel (passive)
		/// </summary>
		protected bool IsServerSocket {
			get { return _isServerSocket; }
			set { _isServerSocket = value; }
		}

		/// <summary>
		/// Gets a value indicating if encryption is in use
		/// </summary>
		public bool SslEnabled {
			get {
				if (this.Connected) {
					return this.SecurteStream.IsEncrypted;
				}

				return false;
			}
		}

		SslPolicyErrors _policyErrors = SslPolicyErrors.None;
		/// <summary>
		/// Gets the SSL errors if there were any
		/// </summary>
		public SslPolicyErrors SslPolicyErrors {
			get { return _policyErrors; }
			private set { _policyErrors = value; }
		}

		X509Certificate _sslCertificate = null;
		/// <summary>
		/// Gets the certificate associated with the current connection
		/// </summary>
		public X509Certificate SslCertificate {
			get { return _sslCertificate; }
			private set { _sslCertificate = value; }
		}

		Socket _sock = null;
		/// <summary>
		/// Connection
		/// </summary>
		protected Socket Socket {
			get {
				if (_sock == null) {
					_sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				}

				return _sock;
			}

			set {
				_sock = value;
			}
		}

		/// <summary>
		/// Default buffer size of the underlying socket
		/// </summary>
		public int RecieveBufferSize {
			get {
				if (this._sock != null) {
					return this._sock.ReceiveBufferSize;
				}

				return 0;
			}

			set {
				if (this._sock != null) {
					this._sock.ReceiveBufferSize = value;
				}
			}
		}

		/// <summary>
		/// Default buffer size of the underlying socket
		/// </summary>
		public int SendBufferSize {
			get {
				if (this._sock != null) {
					return this._sock.SendBufferSize;
				}

				return 0;
			}

			set {
				if (this._sock != null) {
					this._sock.SendBufferSize = value;
				}
			}
		}

		/// <summary>
		/// Local end point
		/// </summary>
		public EndPoint LocalEndPoint {
			get {
				if (_sock != null && _sock.Connected) {
					return _sock.LocalEndPoint;
				}

				return null;
			}
		}

		/// <summary>
		/// Remote end point
		/// </summary>
		public EndPoint RemoteEndPoint {
			get {
				if (_sock != null && _sock.Connected) {
					return _sock.RemoteEndPoint;
				}

				return null;
			}
		}

		/// <summary>
		/// Indicates if there is an active connection
		/// </summary>
		public bool Connected {
			get {
				if (_sock != null)
					return _sock.Connected;
				return false;
			}
		}

		/// <summary>
		/// Gets/Sets the read timeout
		/// </summary>
		public int ReadTimeout {
			get {
				if (this.NetworkStream != null)
					return this.NetworkStream.ReadTimeout;
				return 0;
			}
			set {
				if (this.NetworkStream != null)
					this.NetworkStream.ReadTimeout = value;
			}
		}

		/// <summary>
		/// Gets/Sets the write timeout
		/// </summary>
		public int WriteTimeout {
			get {
				if (this.NetworkStream != null)
					return this.NetworkStream.WriteTimeout;
				return 0;
			}
			set {
				if (this.NetworkStream != null)
					this.NetworkStream.WriteTimeout = value;
			}
		}

		NetworkStream _stream = null;
		/// <summary>
		/// The base stream used for reading and writing the socket
		/// </summary>
		protected NetworkStream NetworkStream {
			get {
				if (_stream == null && this.Connected) {
					_stream = new NetworkStream(this.Socket);
				}

				return _stream;
			}

			private set { _stream = value; }
		}

		/// <summary>
		/// Checks if a certificate is valid
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="certificate"></param>
		/// <param name="chain"></param>
		/// <param name="sslPolicyErrors"></param>
		/// <returns></returns>
		bool CheckCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) {
			// the exception triggered by not returning true here does not
			// propogate properly and ends up causing an obscure error so
			// always return true and check for policy errors after the
			// stream has been setup.
			/*if (sslPolicyErrors == SslPolicyErrors.None) {
				return true;
			}
			return this.IgnoreInvalidSslCertificates;*/

			this.SslPolicyErrors = sslPolicyErrors;
			this.SslCertificate = certificate;

			return true;
		}

		/// <summary>
		/// Authenticates the SSL certificate. This should be called when the stream is switched over
		/// to encryption.
		/// </summary>
		protected void AuthenticateConnection() {
			if (this.Connected && !this.SecurteStream.IsAuthenticated) {
				this.SecurteStream.AuthenticateAsClient(((IPEndPoint)this.RemoteEndPoint).Address.ToString());
				if (this.SslPolicyErrors != Security.SslPolicyErrors.None) {
					InvalidCertificateInfo e = new InvalidCertificateInfo(this);

					this.OnInvalidSslCerticate(this, e);
					if (!e.Ignore) {
						throw new FtpInvalidCertificateException("There were errors validating the SSL certificate: " + this.SslPolicyErrors.ToString());
					}
				}
			}
		}

		SslStream _sslStream = null;
		/// <summary>
		/// Gets a secure stream to the socket.
		/// Intended to be used with the AUTH SSL command
		/// </summary>
		protected SslStream SecurteStream {
			get {
				if (_sslStream == null) {
					this._reader = null;
					this._sslStream = new SslStream(this.NetworkStream, true,
						new RemoteCertificateValidationCallback(CheckCertificate));
				}

				return _sslStream;
			}

			private set { _sslStream = value; }
		}


		/// <summary>
		/// The base stream for reading and writing the socket
		/// </summary>
		public virtual Stream BaseStream {
			get {
				if (this.SecurteStream.IsEncrypted) {
					if (this._reader != null && this._reader.BaseStream == this.NetworkStream) {
						this._reader = new StreamReader(this.SecurteStream);
					}

					return this.SecurteStream;
				}
				else {
					return this.NetworkStream;
				}
			}
		}

		StreamReader _reader = null;
		/// <summary>
		/// Used for easy reading from the socket
		/// </summary>
		protected StreamReader StreamReader {
			get {
				if (_reader == null && this.Connected) {
					_reader = new StreamReader(this.BaseStream, Encoding.Default);
				}

				return _reader;
			}

			private set { _reader = value; }
		}

		string _server = null;
		/// <summary>
		/// The FTP server to connect to
		/// </summary>
		public virtual string Server {
			get { return _server; }
			set { _server = value; }
		}

		int _port = 21;
		/// <summary>
		/// The port the FTP server is listening on
		/// </summary>
		public virtual int Port {
			get { return _port; }
			set { _port = value; }
		}

		public virtual void Connect() {
			this.Connect(this.Server, this.Port);
		}

		/// <summary>
		/// Open a connection
		/// </summary>
		/// <param name="host"></param>
		/// <param name="port"></param>
		public virtual void Connect(string host, int port) {
			if (!this.Connected) {
				this.Server = host;
				this.Port = port;
				this.Disconnect(); // cleans up socket resources before making another connection
				this.Socket.Connect(host, port);
				this.OnConnectionReady();
			}
		}

		/// <summary>
		/// Open a connection
		/// </summary>
		/// <param name="ip"></param>
		/// <param name="port"></param>
		public virtual void Connect(IPAddress ip, int port) {
			if (!this.Connected) {
				this.Server = ip.ToString();
				this.Port = port;
				this.Disconnect(); // cleans up socket resources before making another connection
				this.Socket.Connect(ip, port);
				this.OnConnectionReady();
			}
		}

		/// <summary>
		/// Open a connection
		/// </summary>
		/// <param name="ep"></param>
		public virtual void Connect(EndPoint ep) {
			if (!this.Connected) {

				if (ep is IPEndPoint) {
					IPEndPoint ipep = (IPEndPoint)ep;

					this.Server = ipep.Address.ToString();
					this.Port = ipep.Port;
				}

				this.Disconnect(); // cleans up socket resources before making another connection
				this.Socket.Connect(ep);
				this.OnConnectionReady();
			}
		}

		/// <summary>
		/// Disconnect the socket and free up any resources being used here
		/// </summary>
		public virtual void Disconnect() {
#if DEBUG
			Debug.WriteLine("Called: FtpChannel.Disconnect();");
#endif

			if (this._sock != null) {
				if (this.Connected) {
					this._sock.Shutdown(SocketShutdown.Both);
					this._sock.Close();
				}

				this.OnConnectionClosed();
			}

			if (this._stream != null) {
				this._stream.Close();
				this._stream.Dispose();
			}

			if (this._reader != null) {
				this._reader.Close();
				this._reader.Dispose();
			}

			if (this._sslStream != null) {
				this._sslStream.Close();
				this._sslStream.Dispose();
			}

			this._sock = null;
			this._stream = null;
			this._reader = null;
			this._sslStream = null;
			this._sslCertificate = null;
			this._policyErrors = Security.SslPolicyErrors.None;
		}

		public void Dispose() {
			this.Disconnect();
			this.OnDisposed();
		}
	}
}
