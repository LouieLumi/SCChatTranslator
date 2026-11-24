"""Use the OS trust store plus the bundled Mozilla roots; never disable TLS checks."""
import ssl
import certifi

def tls_context():
    context=ssl.create_default_context()
    context.load_verify_locations(certifi.where())
    return context
