#!/bin/bash

# router-control.sh - CLI wrapper for llama.cpp router mode
# Provides commands to list, load, unload, and switch models via router API
#
# Updated Dec 13, 2025: Added VRAM-aware orchestrator switching

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROUTER_HOST="localhost"
ROUTER_PORT="8081"
ROUTER_URL="http://${ROUTER_HOST}:${ROUTER_PORT}"

# Source VRAM utilities
source "${SCRIPT_DIR}/vram-utils.sh"

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Estimated load times (in seconds)
declare -A LOAD_TIMES=(
    ["coding-reap25b"]=25
    ["agents-nemotron"]=12
    ["agents-qwen3-14b"]=10
    ["agents-coder"]=8
    ["coding-coder"]=8
    ["coding-qwen-7b"]=10
    ["fast-deepseek-lite"]=8
    ["fast-qwen14b"]=10
)

# Helper functions
error() {
    echo -e "${RED}ERROR: $1${NC}" >&2
}

success() {
    echo -e "${GREEN}✓ $1${NC}"
}

warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

info() {
    echo -e "${BLUE}ℹ $1${NC}"
}

header() {
    echo -e "${CYAN}$1${NC}"
}

# Check if router is running
check_router() {
    if ! curl -s -f "${ROUTER_URL}/health" > /dev/null 2>&1; then
        error "Router not responding at ${ROUTER_URL}"
        error "Make sure llama-server is running with --router-config"
        exit 1
    fi
}

# Check if router is running (returns true/false, doesn't exit)
is_router_running() {
    curl -s -f "${ROUTER_URL}/health" > /dev/null 2>&1
}

# Ensure router is running, auto-start if needed
ensure_router_running() {
    if is_router_running; then
        return 0
    fi

    warning "Router not running - auto-starting..."

    # Start the router
    "${SCRIPT_DIR}/start-router.sh" start

    # Verify it started
    if ! is_router_running; then
        error "Failed to auto-start router"
        return 1
    fi

    success "Router auto-started successfully"
    return 0
}

# Check if jq is installed
check_jq() {
    if ! command -v jq &> /dev/null; then
        error "jq is required but not installed"
        error "Install with: sudo apt-get install jq"
        exit 1
    fi
}

# Get estimated load time for preset
get_load_time() {
    local preset="$1"
    echo "${LOAD_TIMES[$preset]:-10}"
}

# Ensure VRAM is available for model, auto-switch orchestrator to CPU if needed
ensure_vram_available() {
    local preset="$1"
    local required_vram_mb free_vram_mb

    required_vram_mb=$(estimate_model_vram_mb "$preset")
    free_vram_mb=$(get_vram_free_mb)

    info "Model '$preset' requires ~${required_vram_mb}MB VRAM"
    info "Current free VRAM: ${free_vram_mb}MB"

    if [[ "$free_vram_mb" -ge "$required_vram_mb" ]]; then
        info "Sufficient VRAM available"
        return 0
    fi

    # NOTE: If using a remote orchestrator, no local switching needed for it

    # Still not enough VRAM - unload existing models to make room
    warning "VRAM insufficient for '$preset' (need ${required_vram_mb}MB, have ${free_vram_mb}MB)"
    info "Checking for loaded models to unload..."

    local loaded_models
    loaded_models=$(curl -s -f "${ROUTER_URL}/models" 2>/dev/null | \
        jq -r '.data[] | select(.status.value == "loaded" or .status.value == "loading") | .id' 2>/dev/null || true)

    if [[ -n "$loaded_models" ]]; then
        warning "Unloading existing models to free VRAM..."
        while IFS= read -r model; do
            if [[ -n "$model" ]]; then
                info "  Unloading: ${model}"
                curl -s -X POST -H "Content-Type: application/json" \
                    -d "{\"model\":\"${model}\"}" \
                    "${ROUTER_URL}/models/unload" > /dev/null 2>&1
                sleep 1
            fi
        done <<< "$loaded_models"

        # Wait for models to fully unload
        local wait_count=0
        local max_wait=30
        while [[ $wait_count -lt $max_wait ]]; do
            local still_loaded
            still_loaded=$(curl -s -f "${ROUTER_URL}/models" 2>/dev/null | \
                jq -r '.data[] | select(.status.value == "loaded" or .status.value == "loading") | .id' 2>/dev/null | wc -l)

            if [[ "$still_loaded" -eq 0 ]]; then
                success "All models unloaded"
                break
            fi

            sleep 1
            wait_count=$((wait_count + 1))
            if [[ $((wait_count % 5)) -eq 0 ]]; then
                info "  Still waiting for unload... ($wait_count seconds)"
            fi
        done

        if [[ $wait_count -ge $max_wait ]]; then
            warning "Timeout waiting for model unload after ${max_wait}s"
        fi

        # Re-check VRAM after unload
        sleep 2
        free_vram_mb=$(get_vram_free_mb)
        info "VRAM after unload: ${free_vram_mb}MB"

        if [[ "$free_vram_mb" -ge "$required_vram_mb" ]]; then
            success "VRAM freed successfully"
            return 0
        fi
    fi

    # Truly insufficient even after unloading everything
    warning "VRAM still insufficient after unloading all models"
    warning "Required: ${required_vram_mb}MB, Available: ${free_vram_mb}MB"
    warning "Proceeding anyway - load may fail or be slow with CPU offload"
    return 0
}

# List available models
cmd_list() {
    check_router
    check_jq

    header "Available Models:"

    local response
    response=$(curl -s -f "${ROUTER_URL}/models" 2>&1) || {
        error "Failed to fetch models list"
        return 1
    }

    # Parse and display models
    echo "$response" | jq -r '.data[] | "  • \(.id)"' || {
        error "Failed to parse models response"
        return 1
    }
}

# Load a model with visual progress
cmd_load() {
    local preset="$1"

    if [[ -z "$preset" ]]; then
        error "Usage: router-control.sh load <preset>"
        return 1
    fi

    check_router
    check_jq

    # VRAM-aware orchestrator management
    ensure_vram_available "$preset" || {
        error "Could not ensure VRAM availability for $preset"
        return 1
    }

    local load_time
    load_time=$(get_load_time "$preset")

    info "Loading model preset: ${preset}"
    info "Estimated load time: ~${load_time}s"

    # Drop page cache to prevent system crawl during model load
    info "Dropping page cache..."
    sync && echo 3 | sudo tee /proc/sys/vm/drop_caches > /dev/null 2>&1 || warning "Could not drop page cache (needs sudo)"

    # Send load request
    local response
    response=$(curl -s -w "\n%{http_code}" -X POST \
        -H "Content-Type: application/json" \
        -d "{\"model\":\"${preset}\"}" \
        "${ROUTER_URL}/models/load" 2>&1) || {
        error "Failed to send load request"
        return 1
    }

    local http_code
    http_code=$(echo "$response" | tail -n1)

    if [[ "$http_code" != "200" ]]; then
        local body
        body=$(echo "$response" | sed '$d')
        if [[ "$http_code" == "404" ]]; then
            error "Model preset '${preset}' not found"
            return 1
        else
            error "Failed to load model (HTTP ${http_code})"
            return 1
        fi
    fi

    # Visual loading progress
    local spinner=('⠋' '⠙' '⠹' '⠸' '⠼' '⠴' '⠦' '⠧' '⠇' '⠏')
    local spin_idx=0
    local elapsed=0
    local status="loading"
    local vram=""

    echo ""
    while [[ "$status" == "loading" ]]; do
        # Get current status
        status=$(curl -s "${ROUTER_URL}/v1/models" 2>/dev/null | \
            jq -r ".data[] | select(.id == \"${preset}\") | .status.value" 2>/dev/null || echo "loading")

        # Get VRAM usage
        if command -v nvidia-smi &> /dev/null; then
            vram=$(nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader 2>/dev/null | head -1)
        fi

        # Display progress
        printf "\r  ${CYAN}${spinner[$spin_idx]}${NC} Loading ${YELLOW}${preset}${NC} [${elapsed}s] ${BLUE}VRAM: ${vram:-N/A}${NC}    "

        spin_idx=$(( (spin_idx + 1) % ${#spinner[@]} ))
        sleep 1
        elapsed=$((elapsed + 1))

        # Timeout after 10 minutes
        if [[ $elapsed -gt 600 ]]; then
            echo ""
            error "Load timeout after 600s"
            return 1
        fi
    done

    echo ""
    if [[ "$status" == "loaded" ]]; then
        success "Model '${preset}' loaded successfully in ${elapsed}s"
        # Final VRAM
        if [[ -n "$vram" ]]; then
            info "Final VRAM: ${vram}"
        fi
    else
        error "Model failed to load (status: ${status})"
        return 1
    fi
}

# Unload a model
cmd_unload() {
    local preset="$1"

    if [[ -z "$preset" ]]; then
        error "Usage: router-control.sh unload <preset>"
        return 1
    fi

    check_router
    check_jq

    info "Unloading model preset: ${preset}"

    local response
    response=$(curl -s -w "\n%{http_code}" -X POST \
        -H "Content-Type: application/json" \
        -d "{\"model\":\"${preset}\"}" \
        "${ROUTER_URL}/models/unload" 2>&1) || {
        error "Failed to send unload request"
        return 1
    }

    local http_code
    http_code=$(echo "$response" | tail -n1)
    local body
    body=$(echo "$response" | sed '$d')

    if [[ "$http_code" == "200" ]]; then
        # Poll for model unload verification
        local wait_count=0
        local max_wait=30
        while [[ $wait_count -lt $max_wait ]]; do
            local status
            status=$(curl -s "${ROUTER_URL}/models" 2>/dev/null | \
                jq -r ".data[] | select(.id == \"${preset}\") | .status.value" 2>/dev/null || echo "loading")
            
            if [[ "$status" != "loaded" && "$status" != "loading" ]]; then
                success "Model unloaded and verified"
                return 0
            fi
            
            sleep 1
            wait_count=$((wait_count + 1))
        done
        
        warning "Model may still be in VRAM after ${max_wait}s"
        return 1
    elif [[ "$http_code" == "404" ]]; then
        error "Model preset '${preset}' not found or not loaded"
        return 1
    else
        error "Failed to unload model (HTTP ${http_code})"
        echo "$body" | jq -r '.error // .message // .' 2>/dev/null || echo "$body"
        return 1
    fi
}

# Show status of loaded models
cmd_status() {
    check_router
    check_jq

    header "Router Status:"

    local response
    response=$(curl -s -f "${ROUTER_URL}/models" 2>&1) || {
        error "Failed to fetch router status"
        return 1
    }

    # Count loaded models
    local loaded_count
    loaded_count=$(echo "$response" | jq '[.data[] | select(.status.value == "loaded")] | length' 2>/dev/null || echo "0")

    if [[ "$loaded_count" -eq 0 ]]; then
        warning "No models currently loaded"
        return 0
    fi

    success "Loaded models: ${loaded_count}"
    echo ""

    # Display loaded models with details
    echo "$response" | jq -r '.data[] | select(.status.value == "loaded") |
        "  \u001b[32m●\u001b[0m \(.id)\n" +
        "    Owner: \(.owned_by)\n" +
        "    Status: \(.status.value)"' || {
        error "Failed to parse status response"
        return 1
    }

    # Estimate VRAM usage (rough estimate)
    echo ""
    info "VRAM estimates:"
    echo "$response" | jq -r '.data[] | select(.status.value == "loaded") | .id' | while read -r model; do
        case "$model" in
            *reap25b*)
                echo "  • ${model}: ~14GB"
                ;;
            *qwen3-14b*|*qwen14b*)
                echo "  • ${model}: ~9GB"
                ;;
            *nemotron*)
                echo "  • ${model}: ~8GB"
                ;;
            *coder*|*deepseek-lite*)
                echo "  • ${model}: ~6GB"
                ;;
            *qwen-7b*)
                echo "  • ${model}: ~5GB"
                ;;
            *)
                echo "  • ${model}: ~8GB (estimated)"
                ;;
        esac
    done
}

# Switch models (unload all, load new)
cmd_switch() {
    local new_preset="$1"
    
    # DEBUG: Log to file for troubleshooting wsl --exec issues

    if [[ -z "$new_preset" ]]; then
        error "Usage: router-control.sh switch <preset>"
        return 1
    fi

    check_jq

    # Auto-start router if not running
    ensure_router_running || {
        error "Cannot switch models - router failed to start"
        return 1
    }

    header "Switching to model: ${new_preset}"

    # Get currently loaded models
    local response
    response=$(curl -s -f "${ROUTER_URL}/models" 2>&1) || {
        error "Failed to fetch current models"
        return 1
    }

    local loaded_models
    loaded_models=$(echo "$response" | jq -r '.data[] | select(.status.value == "loaded" or .status.value == "loading") | .id' 2>/dev/null || true)

    # Unload all current models first
    if [[ -n "$loaded_models" ]]; then
        info "Unloading current models..."
        while IFS= read -r model; do
            if [[ -n "$model" ]]; then
                info "  Unloading: ${model}"
                curl -s -X POST -H "Content-Type: application/json" \
                    -d "{\"model\":\"${model}\"}" \
                    "${ROUTER_URL}/models/unload" > /dev/null 2>&1
                sleep 1
            fi
        done <<< "$loaded_models"
        
        # Wait for ALL models to fully unload (check status, not just sleep)
        info "Waiting for models to fully unload..."
        local wait_count=0
        local max_wait=30  # 30 seconds max wait
        while [[ $wait_count -lt $max_wait ]]; do
            local still_loading
            still_loading=$(curl -s -f "${ROUTER_URL}/models" 2>/dev/null | \
                jq -r '.data[] | select(.status.value == "loaded" or .status.value == "loading") | .id' 2>/dev/null | wc -l)
            
            if [[ "$still_loading" -eq 0 ]]; then
                success "All models unloaded"
                break
            fi
            
            sleep 1
            wait_count=$((wait_count + 1))
            if [[ $((wait_count % 5)) -eq 0 ]]; then
                info "  Still waiting... ($wait_count seconds)"
            fi
        done
        
        if [[ $wait_count -ge $max_wait ]]; then
            warning "Timeout waiting for unload, proceeding anyway"
        fi
        echo ""
    fi

    # Load new model
    cmd_load "$new_preset"
}

# Show usage
usage() {
    cat << EOF
${CYAN}router-control.sh${NC} - llama.cpp router mode control

${YELLOW}Usage:${NC}
  router-control.sh <command> [options]

${YELLOW}Commands:${NC}
  list                  List all available model presets
  load <preset>         Load a specific model preset
  unload <preset>       Unload a specific model preset
  status                Show currently loaded models and VRAM usage
  switch <preset>       Unload all models and load new preset
  help                  Show this help message

${YELLOW}Examples:${NC}
  router-control.sh list
  router-control.sh load coding-reap25b
  router-control.sh status
  router-control.sh switch agents-qwen3-14b
  router-control.sh unload coding-reap25b

${YELLOW}Available Presets:${NC}
  Agents:    agents-qwen3-14b, agents-nemotron, agents-coder
  Coding:    coding-reap25b, coding-coder, coding-qwen-7b
  Fast:      fast-deepseek-lite, fast-qwen14b

${YELLOW}Router:${NC}
  Endpoint: ${ROUTER_URL}

EOF
}

# Main command dispatcher
main() {
    local command="${1:-}"

    case "$command" in
        list)
            cmd_list
            ;;
        load)
            shift
            cmd_load "$@"
            ;;
        unload)
            shift
            cmd_unload "$@"
            ;;
        status)
            cmd_status
            ;;
        switch)
            shift
            cmd_switch "$@"
            ;;
        help|--help|-h|"")
            usage
            ;;
        *)
            error "Unknown command: $command"
            echo ""
            usage
            exit 1
            ;;
    esac
}

main "$@"
