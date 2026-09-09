#!/usr/bin/env python3
"""
Simple HTTP server for crawl4AI that the .NET app can call.
Provides a REST API for crawling GitHub repos and extracting VPN nodes.

Usage:
    python crawl4ai_server.py --port 18888

API:
    GET /crawl?url=<github_url> - Crawl a GitHub repo and return nodes
    GET /crawl-all - Crawl all default repos and return nodes
    GET /health - Health check
"""

import sys
import json
import asyncio
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs
import threading

# Import the crawler
from crawl4ai_nodes import crawl_url, fetch_raw_file, extract_nodes_from_text


class CrawlHandler(BaseHTTPRequestHandler):
    """HTTP request handler for crawling API."""
    
    def do_GET(self):
        """Handle GET requests."""
        parsed = urlparse(self.path)
        
        if parsed.path == '/health':
            self.send_json({"status": "ok", "message": "crawl4ai server running"})
            
        elif parsed.path == '/crawl':
            params = parse_qs(parsed.query)
            url = params.get('url', [None])[0]
            
            if not url:
                self.send_json({"error": "Missing 'url' parameter"}, 400)
                return
            
            # Crawl in background thread
            loop = asyncio.new_event_loop()
            nodes = loop.run_until_complete(crawl_url(url))
            loop.close()
            
            self.send_json({
                "url": url,
                "nodes": nodes,
                "count": len(nodes)
            })
            
        elif parsed.path == '/crawl-all':
            # Crawl all default repos
            urls = [
                'https://github.com/0xRadikal/Free-v2ray-Configs',
                'https://github.com/cbusifabcap/daily_free_vpn',
                'https://github.com/kanaltvyt-dev/FreeForYoung',
                'https://github.com/hello-world-1989/cn-news',
            ]
            
            all_nodes = []
            results = {}
            
            loop = asyncio.new_event_loop()
            for url in urls:
                try:
                    nodes = loop.run_until_complete(crawl_url(url))
                    results[url] = {
                        "count": len(nodes),
                        "nodes": nodes
                    }
                    all_nodes.extend(nodes)
                except Exception as e:
                    results[url] = {"error": str(e), "count": 0, "nodes": []}
            loop.close()
            
            # Deduplicate
            all_nodes = list(set(all_nodes))
            
            self.send_json({
                "results": results,
                "total": len(all_nodes),
                "nodes": all_nodes
            })
            
        elif parsed.path == '/fetch':
            params = parse_qs(parsed.query)
            url = params.get('url', [None])[0]
            
            if not url:
                self.send_json({"error": "Missing 'url' parameter"}, 400)
                return
            
            # Fetch raw file
            loop = asyncio.new_event_loop()
            nodes = loop.run_until_complete(fetch_raw_file(url))
            loop.close()
            
            self.send_json({
                "url": url,
                "nodes": nodes,
                "count": len(nodes)
            })
            
        else:
            self.send_json({"error": "Unknown endpoint"}, 404)
    
    def send_json(self, data, status=200):
        """Send JSON response."""
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Access-Control-Allow-Origin', '*')
        self.end_headers()
        
        response = json.dumps(data, ensure_ascii=False)
        self.wfile.write(response.encode('utf-8'))
    
    def log_message(self, format, *args):
        """Suppress default logging."""
        pass


def run_server(port=18888):
    """Run the HTTP server."""
    server = HTTPServer(('127.0.0.1', port), CrawlHandler)
    print(f"crawl4ai server running on http://127.0.0.1:{port}", file=sys.stderr)
    print("Endpoints:", file=sys.stderr)
    print("  GET /health - Health check", file=sys.stderr)
    print("  GET /crawl?url=<url> - Crawl a GitHub repo", file=sys.stderr)
    print("  GET /crawl-all - Crawl all default repos", file=sys.stderr)
    print("  GET /fetch?url=<raw_url> - Fetch raw file", file=sys.stderr)
    
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down...", file=sys.stderr)
        server.shutdown()


if __name__ == '__main__':
    import argparse
    
    parser = argparse.ArgumentParser(description='crawl4ai HTTP server')
    parser.add_argument('--port', type=int, default=18888, help='Port to listen on')
    
    args = parser.parse_args()
    run_server(args.port)
