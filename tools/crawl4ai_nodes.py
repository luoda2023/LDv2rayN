#!/usr/bin/env python3
"""
Crawl GitHub repos for free VPN nodes using crawl4AI.
This script is called by the .NET app to extract nodes from web pages.

Usage:
    python crawl4ai_nodes.py --url https://github.com/0xRadikal/Free-v2ray-Configs
    python crawl4ai_nodes.py --urls url1 url2 url3 url4

Output:
    Prints one node per line to stdout.
"""

import sys
import json
import re
import asyncio
from pathlib import Path

# Node patterns to match
NODE_PATTERNS = [
    r'vmess://[A-Za-z0-9+/=]+',
    r'vless://[^\s]+',
    r'trojan://[^\s]+',
    r'ss://[A-Za-z0-9+/=]+',
    r'hysteria2://[^\s]+',
    r'tuic://[^\s]+',
    r'socks5?://[^\s]+',
    r'wireguard://[^\s]+',
]


async def crawl_url(url: str) -> list[str]:
    """Crawl a URL and extract VPN nodes."""
    nodes = []
    
    try:
        # Try to use crawl4ai if available
        from crawl4ai import AsyncWebCrawler, BrowserConfig, CrawlerRunConfig
        
        browser_config = BrowserConfig(headless=True)
        run_config = CrawlerRunConfig(
            wait_until="networkidle",
            timeout=30000
        )
        
        async with AsyncWebCrawler(config=browser_config) as crawler:
            result = await crawler.arun(url=url, config=run_config)
            
            if result.success and result.markdown:
                # Extract nodes from markdown content
                nodes = extract_nodes_from_text(result.markdown)
                
    except ImportError:
        # Fallback: use requests + BeautifulSoup
        print(f"[crawl4ai] crawl4ai not installed, using fallback method", file=sys.stderr)
        nodes = await crawl_fallback(url)
    except Exception as e:
        print(f"[crawl4ai] Error crawling {url}: {e}", file=sys.stderr)
        nodes = await crawl_fallback(url)
    
    return nodes


async def crawl_fallback(url: str) -> list[str]:
    """Fallback crawl using requests + BeautifulSoup."""
    nodes = []
    
    try:
        import httpx
        from bs4 import BeautifulSoup
        
        async with httpx.AsyncClient(timeout=30, follow_redirects=True) as client:
            resp = await client.get(url)
            if resp.status_code == 200:
                # Parse HTML
                soup = BeautifulSoup(resp.text, 'html.parser')
                
                # Find all links
                for link in soup.find_all('a', href=True):
                    href = link['href']
                    
                    # Check if it's a raw file link
                    if 'raw.githubusercontent.com' in href:
                        file_nodes = await fetch_raw_file(href)
                        nodes.extend(file_nodes)
                    
                    # Check if it's a blob link (to a text file)
                    elif '/blob/' in href and any(ext in href.lower() for ext in ['.txt', '.yaml', '.yml', '.json']):
                        raw_url = href.replace('/blob/', '/raw/')
                        file_nodes = await fetch_raw_file(raw_url)
                        nodes.extend(file_nodes)
                
                # Also check for nodes directly in page content
                page_nodes = extract_nodes_from_text(resp.text)
                nodes.extend(page_nodes)
                
    except Exception as e:
        print(f"[fallback] Error crawling {url}: {e}", file=sys.stderr)
    
    return list(set(nodes))  # Deduplicate


async def fetch_raw_file(url: str) -> list[str]:
    """Fetch a raw file and extract nodes."""
    nodes = []
    
    try:
        import httpx
        
        # Use mirror if direct fails
        mirrors = [
            url,
            f"https://ghfast.top/{url}",
            f"https://gh-proxy.com/{url}",
            f"https://ghproxy.net/{url}",
        ]
        
        async with httpx.AsyncClient(timeout=15, follow_redirects=True) as client:
            for mirror_url in mirrors:
                try:
                    resp = await client.get(mirror_url)
                    if resp.status_code == 200:
                        text = resp.text
                        file_nodes = extract_nodes_from_text(text)
                        if file_nodes:
                            nodes.extend(file_nodes)
                            break
                except:
                    continue
                    
    except Exception as e:
        print(f"[fallback] Error fetching {url}: {e}", file=sys.stderr)
    
    return nodes


def extract_nodes_from_text(text: str) -> list[str]:
    """Extract VPN nodes from text content."""
    nodes = []
    
    # Try to decode base64 if it looks like a subscription
    if is_base64(text):
        try:
            import base64
            decoded = base64.b64decode(text).decode('utf-8', errors='ignore')
            text = decoded
        except:
            pass
    
    # Find all node patterns
    for pattern in NODE_PATTERNS:
        matches = re.findall(pattern, text)
        nodes.extend(matches)
    
    # Also try line-by-line matching
    for line in text.split('\n'):
        line = line.strip()
        if not line or len(line) < 10:
            continue
        
        for pattern in NODE_PATTERNS:
            if re.match(pattern, line):
                nodes.append(line)
                break
    
    return list(set(nodes))  # Deduplicate


def is_base64(text: str) -> bool:
    """Check if text looks like base64."""
    text = text.strip()
    if len(text) < 100:
        return False
    
    # Check if it's valid base64 characters
    import base64
    try:
        base64.b64decode(text)
        return True
    except:
        return False


async def main():
    """Main entry point."""
    import argparse
    
    parser = argparse.ArgumentParser(description='Crawl GitHub repos for free VPN nodes')
    parser.add_argument('--url', help='Single URL to crawl')
    parser.add_argument('--urls', nargs='+', help='Multiple URLs to crawl')
    parser.add_argument('--output', help='Output file (default: stdout)')
    
    args = parser.parse_args()
    
    urls = []
    if args.url:
        urls = [args.url]
    elif args.urls:
        urls = args.urls
    else:
        # Default: use the 4 specified repos
        urls = [
            'https://github.com/0xRadikal/Free-v2ray-Configs',
            'https://github.com/cbusifabcap/daily_free_vpn',
            'https://github.com/kanaltvyt-dev/FreeForYoung',
            'https://github.com/hello-world-1989/cn-news',
        ]
    
    all_nodes = []
    
    for url in urls:
        print(f"Crawling: {url}", file=sys.stderr)
        nodes = await crawl_url(url)
        print(f"  Found {len(nodes)} nodes", file=sys.stderr)
        all_nodes.extend(nodes)
    
    # Deduplicate
    all_nodes = list(set(all_nodes))
    
    # Output
    if args.output:
        with open(args.output, 'w') as f:
            f.write('\n'.join(all_nodes))
        print(f"Saved {len(all_nodes)} nodes to {args.output}", file=sys.stderr)
    else:
        for node in all_nodes:
            print(node)
    
    return len(all_nodes)


if __name__ == '__main__':
    count = asyncio.run(main())
    sys.exit(0 if count > 0 else 1)
