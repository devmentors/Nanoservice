#!/bin/bash
iptables -t nat -A PREROUTING -p tcp -i eth0 --dport 5000 -j REDIRECT --to-port 5050
iptables -t nat --list